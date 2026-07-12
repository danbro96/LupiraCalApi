using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Composable item-details validation and merge/reclassify semantics over REST: Travel applies only to a
/// Trip and requires ToPlace, unknown enum names are 400s (not silently coerced), same-category updates merge each
/// member wholesale, reclassifying drops the previous details, and a Presence segment round-trips via the
/// top-level Availability field.</summary>
public sealed class ItemDetailsRestTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static CreateCalendarItemRequest Item(Guid calId, string? category = null, ItemDetailsRequest? details = null) => new()
    {
        CalendarId = calId,
        Title = "Trip",
        IsAllDay = false,
        StartsAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
        EndsAt = new DateTimeOffset(2026, 8, 1, 11, 0, 0, TimeSpan.Zero),
        StartTimezone = "UTC",
        Category = category,
        Details = details,
    };

    private static ItemDetailsRequest TripDetails() => new()
    {
        Booking = new BookingDetail(null, null, "BR-1", null, null, null, null),
        Travel = new TravelLegRequest { Mode = TransportMode.Flight, ToPlaceId = Guid.NewGuid(), ToPlace = "Arlanda", ServiceNumber = "SK123", DeparturePoint = "A12" },
    };

    [Fact]
    public async Task Create_trip_with_travel_and_booking_round_trips_and_labels_the_destination()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var resp = await api.PostAsJsonAsync("/items", Item(calId, "Trip", TripDetails()));
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;

        Assert.Equal(ItemCategory.Trip, dto.Category);
        Assert.Equal("SK123", dto.Details!.Travel!.ServiceNumber);
        Assert.Equal("BR-1", dto.Details.Booking!.Reference);
        // Travel carries a resolved ToPlaceId; the free-text ToPlace rides along as the denormalized label.
        Assert.Equal("Arlanda", dto.Details.Travel.ToLabel);
    }

    [Theory]
    [InlineData("Nonsense")]   // unknown name
    [InlineData("99")]         // undefined numeric value must not persist
    public async Task Create_with_unknown_category_is_400(string category)
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var resp = await api.PostAsJsonAsync("/items", Item(calId, category));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Unknown_status_is_400_on_create_and_update()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var create = Item(calId);
        create.Status = "Maybe";
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync("/items", create)).StatusCode);

        var item = await PostItemAsync(api, Item(calId));
        var update = await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest { Status = "Maybe" });
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
    }

    [Fact]
    public async Task Travel_on_a_non_trip_category_is_400()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var resp = await api.PostAsJsonAsync("/items", Item(calId, "Meal", new ItemDetailsRequest
        {
            Travel = new TravelLegRequest { Mode = TransportMode.Flight, ToPlace = "Arlanda" },
        }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("Trip", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Travel_without_to_place_is_400()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var resp = await api.PostAsJsonAsync("/items", Item(calId, "Trip", new ItemDetailsRequest
        {
            Travel = new TravelLegRequest { Mode = TransportMode.Train, Carrier = "SJ" },
        }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("ToPlace", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Update_same_category_merges_member_wholesale_and_keeps_others()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await PostItemAsync(api, Item(calId, "Trip", TripDetails()));

        // Category omitted: validation resolves the item's category; supplying only Travel keeps Booking.
        var resp = await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest
        {
            Details = new ItemDetailsRequest { Travel = new TravelLegRequest { Mode = TransportMode.Flight, ToPlaceId = Guid.NewGuid(), ToPlace = "Arlanda", ServiceNumber = "SK456", Seat = "23A" } },
        });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;

        Assert.Equal("SK456", dto.Details!.Travel!.ServiceNumber);
        Assert.Null(dto.Details.Travel.DeparturePoint);         // member replaced wholesale (was "A12")
        Assert.Equal("BR-1", dto.Details.Booking!.Reference);   // omitted member kept
    }

    [Fact]
    public async Task Reclassify_with_details_replaces_and_without_clears()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var reclassified = await PostItemAsync(api, Item(calId, "Trip", TripDetails()));
        var toAppointment = await api.PutAsJsonAsync($"/items/{reclassified.Id}", new UpdateCalendarItemRequest
        {
            Category = "Appointment",
            Details = new ItemDetailsRequest { Booking = new BookingDetail(null, "CN-9", null, null, null, null, null) },
        });
        toAppointment.EnsureSuccessStatusCode();
        var dto = (await toAppointment.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal(ItemCategory.Appointment, dto.Category);
        Assert.Equal("CN-9", dto.Details!.Booking!.ConfirmationNumber);
        Assert.Null(dto.Details.Travel);   // previous details dropped

        var cleared = await PostItemAsync(api, Item(calId, "Trip", TripDetails()));
        var toGeneral = await api.PutAsJsonAsync($"/items/{cleared.Id}", new UpdateCalendarItemRequest { Category = "General" });
        toGeneral.EnsureSuccessStatusCode();
        var generalDto = (await toGeneral.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal(ItemCategory.General, generalDto.Category);
        Assert.Null(generalDto.Details?.Travel);
        Assert.Null(generalDto.Details?.Booking);
    }

    [Fact]
    public async Task Presence_segment_round_trips_via_the_availability_field()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var create = Item(calId, "General");
        create.Availability = AvailabilityStatus.Office;
        var item = await PostItemAsync(api, create);

        var resp = await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest { Availability = AvailabilityStatus.Sick });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal(AvailabilityStatus.Sick, dto.Details!.Presence!.Status);
    }

    private static async Task<CalendarItemDto> PostItemAsync(HttpClient api, CreateCalendarItemRequest request)
    {
        var resp = await api.PostAsJsonAsync("/items", request);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }
}
