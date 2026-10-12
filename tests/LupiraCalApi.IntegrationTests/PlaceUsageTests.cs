using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LupiraCalApi.Core.Abstractions;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Calendars;
using LupiraCalApi.Core.Dtos.Places;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>GET /place-usage over a stubbed geo: usage comes from readable, accepted, non-cancelled items at a place or a
/// travel endpoint, recurrences counted up to <c>to</c>, merged ids folded into their survivor.</summary>
public sealed class PlaceUsageTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Alice = "alice@x.test";
    private const string Bob = "bob@x.test";
    private const string Window = "from=2026-01-01T00:00:00Z&to=2026-06-01T00:00:00Z";
    private static readonly DateTimeOffset Jan5 = new(2026, 1, 5, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Gym = Guid.NewGuid();
    private static readonly Guid GymDuplicate = Guid.NewGuid();
    private static readonly Guid Cabin = Guid.NewGuid();
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly Guid Arlanda = Guid.NewGuid();

    private sealed class StubGeo : IGeoResolver
    {
        private static readonly Dictionary<Guid, GeoPlaceSummary> Places = new()
        {
            [Gym] = new(Gym, "Gym", 59.33, 18.07),
            [GymDuplicate] = new(Gym, "Gym", 59.33, 18.07),
            [Cabin] = new(Cabin, "Cabin", 57.0, 13.5),
            [Office] = new(Office, "Office", 59.34, 18.05),
            [Arlanda] = new(Arlanda, "Arlanda", 59.65, 17.93),
        };

        public bool IsConfigured => true;

        public Task<GeoPlaceResolution?> ResolveAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<GeoPlaceResolution?>(null);

        public Task<IReadOnlyDictionary<Guid, GeoPlaceSummary>?> LookupAsync(IReadOnlyCollection<Guid> placeIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, GeoPlaceSummary>?>(placeIds.Where(Places.ContainsKey).ToDictionary(id => id, id => Places[id]));

        public Task<GeoPlaceSummary?> NearestAsync(double latitude, double longitude, int radiusM, CancellationToken ct = default) =>
            Task.FromResult<GeoPlaceSummary?>(null);

        public Task<GeoReverseLabel?> ReverseAsync(double latitude, double longitude, CancellationToken ct = default) =>
            Task.FromResult<GeoReverseLabel?>(null);
    }

    private Func<string, HttpClient> Clients()
    {
        var app = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IGeoResolver>(new StubGeo())));
        return email =>
        {
            var api = app.CreateClient();
            api.DefaultRequestHeaders.Add("X-Dev-User", email);
            return api;
        };
    }

    private static CreateCalendarItemRequest Visit(Guid calId, Guid? placeId, DateTimeOffset start, string category = "Activity", string? rrule = null, string? status = null) => new()
    {
        CalendarId = calId,
        Title = "Visit",
        Category = category,
        IsAllDay = false,
        StartsAt = start,
        EndsAt = start.AddHours(1),
        StartTimezone = "UTC",
        Location = placeId is null ? null : "Somewhere",
        PlaceId = placeId,
        RecurrenceRule = rrule,
        Status = status,
    };

    private static async Task<CalendarItemDto> CreateAsync(HttpClient api, CreateCalendarItemRequest item) =>
        (await (await api.PostAsJsonAsync("/items", item)).EnsureSuccessStatusCode().Content.ReadFromJsonAsync<CalendarItemDto>())!;

    private static async Task<List<PlaceUsageDto>> UsageAsync(HttpClient api, string query = "") =>
        (await api.GetFromJsonAsync<List<PlaceUsageDto>>($"/place-usage?{Window}{query}"))!;

    [Fact]
    public async Task Usage_comes_from_readable_calendars_only()
    {
        var client = Clients();
        var alice = client(Alice);
        var bob = client(Bob);
        var own = await CreateCalendarAsync(alice, "mine", "Mine");
        var family = await CreateCalendarAsync(bob, "family", "Family");
        var bobsPrivate = await CreateCalendarAsync(bob, "private", "Private");
        (await bob.PostAsJsonAsync($"/calendars/{family}/owners", new GrantOwnerRequest { Email = Alice, Access = "read" })).EnsureSuccessStatusCode();
        await CreateAsync(alice, Visit(own, Office, Jan5));
        await CreateAsync(bob, Visit(family, Gym, Jan5));
        await CreateAsync(bob, Visit(bobsPrivate, Cabin, Jan5));

        Assert.True((await UsageAsync(alice)).Select(u => u.PlaceId).ToHashSet().SetEquals([Office, Gym]));
        Assert.Equal(Gym, Assert.Single(await UsageAsync(alice, $"&calendarId={family}")).PlaceId);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/place-usage?{Window}&calendarId={bobsPrivate}")).StatusCode);
        Assert.Equal(Cabin, Assert.Single(await UsageAsync(bob, $"&calendarId={bobsPrivate}")).PlaceId);
    }

    [Fact]
    public async Task Folds_places_travel_endpoints_categories_and_companions()
    {
        var api = Clients()(Alice);
        var calId = await CreateCalendarAsync(api);
        var (anna, johan) = (Guid.NewGuid(), Guid.NewGuid());
        var weekly = await CreateAsync(api, Visit(calId, Gym, new DateTimeOffset(2026, 5, 4, 18, 0, 0, TimeSpan.Zero), rrule: "FREQ=WEEKLY"));
        (await api.PutAsJsonAsync($"/items/{weekly.Id}/participants", new SetParticipantsRequest { ContactIds = [anna, johan] })).EnsureSuccessStatusCode();
        var meal = await CreateAsync(api, Visit(calId, GymDuplicate, Jan5, "Meal"));
        (await api.PutAsJsonAsync($"/items/{meal.Id}/participants", new SetParticipantsRequest { ContactIds = [anna] })).EnsureSuccessStatusCode();
        await CreateAsync(api, Visit(calId, Gym, Jan5.AddDays(1), status: "Cancelled"));
        var trip = Visit(calId, null, Jan5.AddDays(2), "Trip");
        trip.Details = new ItemDetailsRequest { Travel = new TravelLegRequest { Mode = TransportMode.Flight, FromPlaceId = Arlanda, ToPlaceId = Cabin } };
        await CreateAsync(api, trip);

        var usage = await UsageAsync(api);

        Assert.Equal(3, usage.Count);
        var gym = usage[0];
        Assert.Equal((Gym, "Gym", 5, new DateOnly(2026, 5, 25)), (gym.PlaceId, gym.Label, gym.EventCount, gym.LastDay));
        Assert.Equal(
            [(ItemCategory.Activity, 4, new DateOnly(2026, 5, 25)), (ItemCategory.Meal, 1, new DateOnly(2026, 1, 5))],
            gym.ByCategory.Select(c => (c.Category, c.EventCount, c.LastDay)));
        Assert.Equal([(anna, 5), (johan, 4)], gym.Companions.Select(c => (c.ContactId, c.EventCount)));
        Assert.All(usage.Skip(1), u => Assert.Equal((1, new DateOnly(2026, 1, 7), ItemCategory.Trip), (u.EventCount, u.LastDay, Assert.Single(u.ByCategory).Category)));
        Assert.True(usage.Skip(1).Select(u => (u.PlaceId, u.Label)).ToHashSet().SetEquals([(Cabin, "Cabin"), (Arlanda, "Arlanda")]));

        using var raw = JsonDocument.Parse(await api.GetStringAsync($"/place-usage?{Window}"));
        var first = raw.RootElement[0];
        Assert.Equal("Activity", first.GetProperty("byCategory")[0].GetProperty("category").GetString());
        Assert.Equal("2026-05-25", first.GetProperty("lastDay").GetString());
    }

    [Theory]
    [InlineData("from=2026-06-02T00:00:00Z&to=2026-06-01T00:00:00Z")]
    [InlineData("limit=0")]
    [InlineData("limit=501")]
    public async Task Rejects_bad_parameters(string query)
    {
        var api = Clients()(Alice);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync($"/place-usage?{query}")).StatusCode);
    }

    [Fact]
    public async Task Requires_authentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Factory.CreateClient().GetAsync("/place-usage")).StatusCode);
    }
}
