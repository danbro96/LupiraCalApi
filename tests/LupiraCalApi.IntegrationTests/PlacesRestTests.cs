using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Places;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>REST discovery over the shared location catalog: name/kind search, and the place→items reverse
/// index (location + travel/car endpoints), scoped to calendars the caller can read.</summary>
public sealed class PlacesRestTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static async Task<CalendarItemDto> CreateAtAsync(HttpClient api, Guid calId, string title, string location)
    {
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = calId, Title = title, IsAllDay = false,
            StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC", Location = location,
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    [Fact]
    public async Task Search_matches_name_case_insensitively()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        await CreateAtAsync(api, calId, "Coffee", "Cafe Central");
        await CreateAtAsync(api, calId, "Gym", "Riverside Gym");

        var hits = await api.GetFromJsonAsync<List<PlaceDto>>("/places?search=central");
        Assert.Contains(hits!, p => p.Name == "Cafe Central");
        Assert.DoesNotContain(hits!, p => p.Name == "Riverside Gym");
    }

    [Fact]
    public async Task Search_filters_by_kind()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        await CreateAtAsync(api, calId, "Coffee", "Cafe Central");

        // Free-text locations resolve to Venue nodes; no cities exist unless created explicitly.
        var venues = await api.GetFromJsonAsync<List<PlaceDto>>("/places?kind=Venue");
        Assert.Contains(venues!, p => p.Name == "Cafe Central");
        var cities = await api.GetFromJsonAsync<List<PlaceDto>>("/places?kind=City");
        Assert.DoesNotContain(cities!, p => p.Name == "Cafe Central");
    }

    [Fact]
    public async Task Place_items_returns_items_anchored_here()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateAtAsync(api, calId, "Coffee", "Cafe Central");
        var placeId = item.PlaceId!.Value;

        var items = await api.GetFromJsonAsync<List<CalendarItemDto>>($"/places/{placeId}/items");
        Assert.Contains(items!, i => i.Id == item.Id);
    }

    [Fact]
    public async Task Place_items_includes_travel_endpoints()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = calId, Title = "Flight", IsAllDay = false,
            StartsAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 8, 1, 11, 0, 0, TimeSpan.Zero), StartTimezone = "UTC",
            Kind = "Flight",
            KindDetails = new ItemKindDetailsRequest
            {
                Travel = new TravelDetailRequest { ToPlace = "Arlanda" },
                Flight = new FlightDetail("SK1", null, null, null, null, null),
            },
        });
        resp.EnsureSuccessStatusCode();
        var flight = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        var toPlaceId = flight.KindDetails!.Travel!.ToPlaceId;

        var items = await api.GetFromJsonAsync<List<CalendarItemDto>>($"/places/{toPlaceId}/items");
        Assert.Contains(items!, i => i.Id == flight.Id);
    }

    [Fact]
    public async Task Place_items_hides_items_from_calendars_you_cannot_read()
    {
        var alice = Factory.ApiClient("alice@x.test");
        var calId = await CreateCalendarAsync(alice);
        var item = await CreateAtAsync(alice, calId, "Coffee", "Cafe Central");
        var placeId = item.PlaceId!.Value;

        var bob = Factory.ApiClient("bob@x.test");
        var items = await bob.GetFromJsonAsync<List<CalendarItemDto>>($"/places/{placeId}/items");
        Assert.DoesNotContain(items!, i => i.Id == item.Id);
    }
}
