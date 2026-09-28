using System.Net;
using System.Net.Http.Json;
using LupiraCalApi.Core.Abstractions;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Calendars;
using LupiraCalApi.Core.Dtos.Hotspots;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>GET /hotspots over stubbed geo + photo sources: event days come from readable, accepted, non-cancelled items
/// (recurrences and multi-day spans expanded), photo cells merge in by proximity, and each geo place anchors one hotspot.</summary>
public sealed class HotspotsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Alice = "alice@x.test";
    private const string Bob = "bob@x.test";
    private const string Window = "to=2026-06-01T00:00:00Z";
    private static readonly DateTimeOffset Jan5 = new(2026, 1, 5, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Gym = Guid.NewGuid();
    private static readonly Guid Cabin = Guid.NewGuid();
    private static readonly Guid Office = Guid.NewGuid();

    private sealed class StubGeo : IGeoResolver
    {
        private readonly Dictionary<Guid, GeoPlaceSummary> _places = new()
        {
            [Gym] = new(Gym, "Gym", 59.3300, 18.0700),
            [Cabin] = new(Cabin, "Cabin", 57.0000, 13.5000),
            [Office] = new(Office, "Office", 59.3400, 18.0500),
        };

        public GeoPlaceSummary? Nearest { get; init; }

        public bool IsConfigured => true;

        public Task<GeoPlaceResolution?> ResolveAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<GeoPlaceResolution?>(null);

        public Task<IReadOnlyDictionary<Guid, GeoPlaceSummary>?> LookupAsync(IReadOnlyCollection<Guid> placeIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, GeoPlaceSummary>?>(placeIds.Where(_places.ContainsKey).ToDictionary(id => id, id => _places[id]));

        public Task<GeoPlaceSummary?> NearestAsync(double latitude, double longitude, int radiusM, CancellationToken ct = default) =>
            Task.FromResult(Nearest);

        public Task<GeoReverseLabel?> ReverseAsync(double latitude, double longitude, CancellationToken ct = default) =>
            Task.FromResult<GeoReverseLabel?>(new GeoReverseLabel("5, Kyrkogatan, Ljungby, Sverige", "Ljungby"));
    }

    private sealed class StubPhotos(IReadOnlyList<PhotoDensityCell>? cells) : IPhotoDensitySource
    {
        public Task<IReadOnlyList<PhotoDensityCell>?> CellsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default) =>
            Task.FromResult(cells);
    }

    private Func<string, HttpClient> Clients(StubGeo? geo = null, IReadOnlyList<PhotoDensityCell>? cells = null, bool photosDown = false)
    {
        var app = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton<IGeoResolver>(geo ?? new StubGeo());
            s.AddSingleton<IPhotoDensitySource>(new StubPhotos(photosDown ? null : cells ?? []));
        }));
        return email =>
        {
            var api = app.CreateClient();
            api.DefaultRequestHeaders.Add("X-Dev-User", email);
            return api;
        };
    }

    private static CreateCalendarItemRequest Visit(Guid? calId, Guid placeId, DateTimeOffset start, string? rrule = null, string? status = null) => new()
    {
        CalendarId = calId,
        Title = "Visit",
        IsAllDay = false,
        StartsAt = start,
        EndsAt = start.AddHours(1),
        StartTimezone = "UTC",
        Location = "Somewhere",
        PlaceId = placeId,
        RecurrenceRule = rrule,
        Status = status,
    };

    private static async Task<CalendarItemDto> CreateAsync(HttpClient api, CreateCalendarItemRequest item) =>
        (await (await api.PostAsJsonAsync("/items", item)).EnsureSuccessStatusCode().Content.ReadFromJsonAsync<CalendarItemDto>())!;

    private static async Task<List<HotspotDto>> HotspotsAsync(HttpClient api, string query = "") =>
        (await api.GetFromJsonAsync<List<HotspotDto>>($"/hotspots?{Window}{query}"))!;

    private static PhotoDensityCell Cell(double lat, double lon, int count, params int[] dayOffsets) =>
        new(lat, lon, count, [.. dayOffsets.Select(d => new DateOnly(2026, 3, 1).AddDays(d))]);

    [Fact]
    public async Task A_weekly_recurrence_counts_every_week_and_anchors_to_its_place()
    {
        var api = Clients()(Alice);
        var calId = await CreateCalendarAsync(api);
        await CreateAsync(api, Visit(calId, Gym, Jan5, "FREQ=WEEKLY;COUNT=5"));

        var hotspot = Assert.Single(await HotspotsAsync(api));

        Assert.Equal($"place:{Gym}", hotspot.Id);
        Assert.Equal(Gym, hotspot.PlaceId);
        Assert.Equal("Gym", hotspot.Label);
        Assert.Equal((5, 5, 0), (hotspot.EventCount, hotspot.ActiveDays, hotspot.PhotoCount));
        Assert.Equal((new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 2)), (hotspot.FirstDay, hotspot.LastDay));
    }

    [Fact]
    public async Task A_multi_day_trip_counts_every_day_at_its_destination()
    {
        var api = Clients()(Alice);
        var calId = await CreateCalendarAsync(api);
        await CreateAsync(api, new CreateCalendarItemRequest
        {
            CalendarId = calId,
            Title = "Cabin week",
            Category = "Trip",
            IsAllDay = true,
            StartDate = new DateOnly(2026, 2, 10),
            EndDate = new DateOnly(2026, 2, 13),
            Details = new ItemDetailsRequest { Travel = new TravelLegRequest { Mode = TransportMode.Car, ToPlaceId = Cabin } },
        });

        var hotspot = Assert.Single(await HotspotsAsync(api));

        Assert.Equal(Cabin, hotspot.PlaceId);
        Assert.Equal((1, 4), (hotspot.EventCount, hotspot.ActiveDays));
    }

    [Fact]
    public async Task Events_come_from_readable_calendars_only()
    {
        var client = Clients();
        var alice = client(Alice);
        var bob = client(Bob);
        var own = await CreateCalendarAsync(alice, "mine", "Mine");
        var family = await CreateCalendarAsync(bob, "family", "Family");
        var bobsPrivate = await CreateCalendarAsync(bob, "private", "Private");
        (await bob.PostAsJsonAsync($"/calendars/{family}/owners", new GrantOwnerRequest { Email = Alice, Access = "read" })).EnsureSuccessStatusCode();
        for (var week = 0; week < 3; week++)
        {
            await CreateAsync(alice, Visit(own, Office, Jan5.AddDays(7 * week)));
            await CreateAsync(bob, Visit(family, Gym, Jan5.AddDays(7 * week)));
            await CreateAsync(bob, Visit(bobsPrivate, Cabin, Jan5.AddDays(7 * week)));
        }

        var places = (await HotspotsAsync(alice)).Select(h => h.PlaceId).ToHashSet();
        Assert.True(places.SetEquals([Office, Gym]));

        var narrowed = Assert.Single(await HotspotsAsync(alice, $"&calendarId={family}"));
        Assert.Equal(Gym, narrowed.PlaceId);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/hotspots?{Window}&calendarId={bobsPrivate}")).StatusCode);
    }

    [Fact]
    public async Task Cancelled_and_proposed_items_do_not_count()
    {
        var api = Clients()(Alice);
        var calId = await CreateCalendarAsync(api);
        await CreateAsync(api, Visit(calId, Gym, Jan5));
        await CreateAsync(api, Visit(calId, Gym, Jan5.AddDays(7)));
        await CreateAsync(api, Visit(calId, Gym, Jan5.AddDays(14), status: "Cancelled"));
        var proposed = await CreateAsync(api, Visit(null, Gym, Jan5.AddDays(21)));
        (await api.PostAsync($"/items/{proposed.Id}/calendars/{calId}?status=proposed", null)).EnsureSuccessStatusCode();

        Assert.Empty(await HotspotsAsync(api));
        Assert.Equal(2, Assert.Single(await HotspotsAsync(api, "&minDays=2")).ActiveDays);
    }

    [Fact]
    public async Task Nearby_photo_days_merge_into_the_event_place_and_photo_only_spots_get_a_label()
    {
        var api = Clients(cells:
        [
            Cell(59.3309, 18.0700, 40, 0, 1),
            Cell(58.0000, 14.0000, 300, 2, 3, 4),
        ])(Alice);
        var calId = await CreateCalendarAsync(api);
        await CreateAsync(api, Visit(calId, Gym, Jan5));

        var hotspots = await HotspotsAsync(api);

        var gym = Assert.Single(hotspots, h => h.PlaceId == Gym);
        Assert.Equal((1, 40, 3), (gym.EventCount, gym.PhotoCount, gym.ActiveDays));
        var spot = Assert.Single(hotspots, h => h.PlaceId is null);
        Assert.Equal("cell:58.000,14.000", spot.Id);
        Assert.Equal("Kyrkogatan 5, Ljungby", spot.Label);
        Assert.Equal((0, 300, 3), (spot.EventCount, spot.PhotoCount, spot.ActiveDays));
    }

    [Fact]
    public async Task A_nearby_place_anchors_only_one_photo_only_hotspot()
    {
        var beach = new GeoPlaceSummary(Guid.NewGuid(), "Beach", 58.0, 14.0);
        var api = Clients(new StubGeo { Nearest = beach },
        [
            Cell(58.0000, 14.0000, 10, 0, 1, 2, 3),
            Cell(56.0000, 12.0000, 10, 0, 1, 2),
        ])(Alice);

        var hotspots = await HotspotsAsync(api);

        Assert.Equal(2, hotspots.Count);
        Assert.Equal(("Beach", beach.PlaceId), (hotspots[0].Label, hotspots[0].PlaceId));
        Assert.Equal(("Kyrkogatan 5, Ljungby", null), (hotspots[1].Label, hotspots[1].PlaceId));
    }

    [Fact]
    public async Task Photos_unavailable_degrades_to_events_only()
    {
        var api = Clients(photosDown: true)(Alice);
        var calId = await CreateCalendarAsync(api);
        await CreateAsync(api, Visit(calId, Gym, Jan5, "FREQ=DAILY;COUNT=3"));

        Assert.Equal(Gym, Assert.Single(await HotspotsAsync(api)).PlaceId);
    }

    [Theory]
    [InlineData("from=2026-06-02T00:00:00Z&to=2026-06-01T00:00:00Z")]
    [InlineData("minDays=0")]
    [InlineData("limit=501")]
    public async Task Rejects_bad_parameters(string query)
    {
        var api = Clients()(Alice);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync($"/hotspots?{query}")).StatusCode);
    }
}
