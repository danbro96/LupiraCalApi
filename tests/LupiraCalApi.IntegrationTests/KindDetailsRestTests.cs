using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Places;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Kind-details request validation and merge/reclassify semantics over REST: members must match the
/// kind, Travel requires ToPlace, unknown enum names are 400s (not silently coerced), same-kind updates merge
/// member-wholesale, and reclassifying drops the previous kind's details.</summary>
public sealed class KindDetailsRestTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static CreateCalendarItemRequest Item(Guid calId, string? kind = null, ItemKindDetailsRequest? details = null) => new()
    {
        CalendarId = calId,
        Title = "Trip",
        IsAllDay = false,
        StartsAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
        EndsAt = new DateTimeOffset(2026, 8, 1, 11, 0, 0, TimeSpan.Zero),
        StartTimezone = "UTC",
        Kind = kind,
        KindDetails = details,
    };

    private static ItemKindDetailsRequest FlightDetails() => new()
    {
        Travel = new TravelDetailRequest { ToPlace = "Arlanda", BookingReference = "BR-1" },
        Flight = new FlightDetail("SK123", null, "A12", null, null, null),
    };

    [Fact]
    public async Task Create_flight_with_travel_and_flight_round_trips_and_resolves_place()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var resp = await api.PostAsJsonAsync("/items", Item(calId, "Flight", FlightDetails()));
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;

        Assert.Equal(ItemKind.Flight, dto.Kind);
        Assert.Equal("SK123", dto.KindDetails!.Flight!.FlightNumber);
        Assert.Equal("BR-1", dto.KindDetails.Travel!.BookingReference);
        Assert.NotEqual(Guid.Empty, dto.KindDetails.Travel.ToPlaceId);

        var place = await api.GetFromJsonAsync<PlaceDto>($"/places/{dto.KindDetails.Travel.ToPlaceId}");
        Assert.Equal("Arlanda", place!.Name);
    }

    [Theory]
    [InlineData("Meeting")]   // unknown name
    [InlineData("99")]        // undefined numeric value must not persist
    public async Task Create_with_unknown_kind_is_400(string kind)
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var resp = await api.PostAsJsonAsync("/items", Item(calId, kind));
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
    public async Task Mismatched_member_is_400()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var resp = await api.PostAsJsonAsync("/items", Item(calId, "Bill", new ItemKindDetailsRequest
        {
            Flight = new FlightDetail("SK123", null, null, null, null, null),
        }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("KindDetails.Flight", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Travel_without_to_place_is_400()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var resp = await api.PostAsJsonAsync("/items", Item(calId, "Travel", new ItemKindDetailsRequest
        {
            Travel = new TravelDetailRequest { Carrier = "SAS" },
        }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("ToPlace", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Availability_field_on_non_availability_kind_is_400_on_create_and_update()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var create = Item(calId, "Bill");
        create.Availability = AvailabilityStatus.Sick;
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync("/items", create)).StatusCode);

        var item = await PostItemAsync(api, Item(calId, "Bill"));
        var update = await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest { Availability = AvailabilityStatus.Sick });
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
    }

    [Fact]
    public async Task Update_same_kind_merges_member_wholesale_and_keeps_others()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await PostItemAsync(api, Item(calId, "Flight", FlightDetails()));

        // r.Kind omitted: validation must resolve the item's kind; supplying only Flight keeps Travel.
        var resp = await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest
        {
            KindDetails = new ItemKindDetailsRequest { Flight = new FlightDetail("SK456", null, null, null, null, "23kg") },
        });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;

        Assert.Equal("SK456", dto.KindDetails!.Flight!.FlightNumber);
        Assert.Null(dto.KindDetails.Flight.Gate);                       // member replaced wholesale
        Assert.Equal("BR-1", dto.KindDetails.Travel!.BookingReference); // omitted member kept
    }

    [Fact]
    public async Task Reclassify_with_details_replaces_and_without_clears()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var reclassified = await PostItemAsync(api, Item(calId, "Flight", FlightDetails()));
        var toAppointment = await api.PutAsJsonAsync($"/items/{reclassified.Id}", new UpdateCalendarItemRequest
        {
            Kind = "Appointment",
            KindDetails = new ItemKindDetailsRequest { Appointment = new AppointmentDetailRequest { AppointmentType = "dentist" } },
        });
        toAppointment.EnsureSuccessStatusCode();
        var dto = (await toAppointment.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal(ItemKind.Appointment, dto.Kind);
        Assert.Equal("dentist", dto.KindDetails!.Appointment!.AppointmentType);
        Assert.Null(dto.KindDetails.Travel);   // previous kind's details dropped
        Assert.Null(dto.KindDetails.Flight);

        var cleared = await PostItemAsync(api, Item(calId, "Flight", FlightDetails()));
        var toGeneric = await api.PutAsJsonAsync($"/items/{cleared.Id}", new UpdateCalendarItemRequest { Kind = "Generic" });
        toGeneric.EnsureSuccessStatusCode();
        var genericDto = (await toGeneric.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal(ItemKind.Generic, genericDto.Kind);
        Assert.Null(genericDto.KindDetails?.Travel);
        Assert.Null(genericDto.KindDetails?.Flight);
    }

    [Fact]
    public async Task Update_availability_item_with_only_availability_succeeds()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var create = Item(calId, "Availability");
        create.Availability = AvailabilityStatus.Office;
        var item = await PostItemAsync(api, create);

        var resp = await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest { Availability = AvailabilityStatus.Sick });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal(AvailabilityStatus.Sick, dto.KindDetails!.Availability!.Status);
    }

    private static async Task<CalendarItemDto> PostItemAsync(HttpClient api, CreateCalendarItemRequest request)
    {
        var resp = await api.PostAsJsonAsync("/items", request);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }
}
