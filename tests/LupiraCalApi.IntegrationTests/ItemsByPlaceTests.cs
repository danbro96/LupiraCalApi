using System.Net.Http.Json;
using LupiraCalApi.Core.Abstractions;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>GET /items/by-place/{id} lists items anchored to a place as location or travel endpoint, following geo merge
/// redirects: items still stored against a merged-away id belong to its survivor.</summary>
public sealed class ItemsByPlaceTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Alice = "alice@x.test";
    private static readonly DateTimeOffset Noon = new(2026, 3, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Arlanda = Guid.NewGuid();
    private static readonly Guid ArlandaDuplicate = Guid.NewGuid();
    private static readonly Guid Riga = Guid.NewGuid();

    private sealed class MergingGeo(bool down = false) : IGeoResolver
    {
        public bool IsConfigured => true;

        public Task<GeoPlaceResolution?> ResolveAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<GeoPlaceResolution?>(null);

        public Task<IReadOnlyDictionary<Guid, GeoPlaceSummary>?> LookupAsync(IReadOnlyCollection<Guid> placeIds, CancellationToken ct = default)
        {
            if (down) return Task.FromResult<IReadOnlyDictionary<Guid, GeoPlaceSummary>?>(null);
            var survivor = new GeoPlaceSummary(Arlanda, "Arlanda", 59.65, 17.93);
            var riga = new GeoPlaceSummary(Riga, "Riga Airport", 56.92, 23.98);
            return Task.FromResult<IReadOnlyDictionary<Guid, GeoPlaceSummary>?>(placeIds.ToDictionary(
                id => id, id => id == Riga ? riga : survivor));
        }

        public Task<GeoPlaceSummary?> NearestAsync(double latitude, double longitude, int radiusM, CancellationToken ct = default) =>
            Task.FromResult<GeoPlaceSummary?>(null);

        public Task<GeoReverseLabel?> ReverseAsync(double latitude, double longitude, CancellationToken ct = default) =>
            Task.FromResult<GeoReverseLabel?>(null);
    }

    private HttpClient Client(IGeoResolver geo)
    {
        var api = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(geo))).CreateClient();
        api.DefaultRequestHeaders.Add("X-Dev-User", Alice);
        return api;
    }

    private static CreateCalendarItemRequest Flight(Guid calId, string title, Guid from, Guid to) => new()
    {
        CalendarId = calId,
        Title = title,
        Category = "Trip",
        IsAllDay = false,
        StartsAt = Noon,
        EndsAt = Noon.AddHours(2),
        StartTimezone = "UTC",
        Details = new ItemDetailsRequest { Travel = new TravelLegRequest { Mode = TransportMode.Flight, FromPlaceId = from, ToPlaceId = to } },
    };

    private static CreateCalendarItemRequest Meeting(Guid calId, Guid placeId) => new()
    {
        CalendarId = calId,
        Title = "Check-in",
        IsAllDay = false,
        StartsAt = Noon.AddHours(-1),
        EndsAt = Noon,
        StartTimezone = "UTC",
        Location = "Arlanda",
        PlaceId = placeId,
    };

    // Written without geo so the stored ids predate the merge; a write through geo stores the survivor.
    private async Task SeedBeforeMergeAsync()
    {
        var api = Factory.ApiClient(Alice);
        var calId = await CreateCalendarAsync(api);
        (await api.PostAsJsonAsync("/items", Flight(calId, "Out", Arlanda, Riga))).EnsureSuccessStatusCode();
        (await api.PostAsJsonAsync("/items", Flight(calId, "Home", Riga, ArlandaDuplicate))).EnsureSuccessStatusCode();
        (await api.PostAsJsonAsync("/items", Meeting(calId, ArlandaDuplicate))).EnsureSuccessStatusCode();
    }

    private static async Task<List<string>> TitlesAtAsync(HttpClient api, Guid placeId) =>
        [.. (await api.GetFromJsonAsync<List<CalendarItemDto>>($"/items/by-place/{placeId}"))!.Select(i => i.Title).Order()];

    [Fact]
    public async Task Items_on_a_merged_away_place_are_listed_under_its_survivor()
    {
        await SeedBeforeMergeAsync();
        var api = Client(new MergingGeo());

        Assert.Equal(["Check-in", "Home", "Out"], await TitlesAtAsync(api, Arlanda));
        Assert.Equal(["Check-in", "Home", "Out"], await TitlesAtAsync(api, ArlandaDuplicate));
        Assert.Equal(["Home", "Out"], await TitlesAtAsync(api, Riga));
    }

    [Fact]
    public async Task Geo_unavailable_falls_back_to_exact_ids()
    {
        await SeedBeforeMergeAsync();
        var api = Client(new MergingGeo(down: true));

        Assert.Equal(["Out"], await TitlesAtAsync(api, Arlanda));
        Assert.Equal(["Check-in", "Home"], await TitlesAtAsync(api, ArlandaDuplicate));
    }
}
