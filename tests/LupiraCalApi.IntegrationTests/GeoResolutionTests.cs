using System.Net.Http.Json;
using LupiraCalApi.Application;
using LupiraCalApi.Dtos.CalendarItems;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Location resolution goes through <see cref="IGeoResolver"/> (LupiraGeoApi) — there is no local Place
/// catalog. Geo unconfigured ⇒ the denormalized label is the raw text and the id is null; geo configured ⇒ the item
/// carries the geo place id + canonical label.</summary>
public sealed class GeoResolutionTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private sealed class StubGeo(Guid id, string name) : IGeoResolver
    {
        public bool IsConfigured => true;
        public Task<GeoPlaceResolution?> ResolveAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<GeoPlaceResolution?>(new GeoPlaceResolution(id, name, 59.33, 18.06));
    }

    private static CreateCalendarItemRequest Coffee(Guid calId, string location) => new()
    {
        CalendarId = calId, Title = "Coffee", IsAllDay = false,
        StartsAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
        EndsAt = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero), StartTimezone = "UTC", Location = location,
    };

    [Fact]
    public async Task Geo_unconfigured_stores_raw_text_label_and_no_place_id()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var resp = await api.PostAsJsonAsync("/items", Coffee(calId, "Cafe Central"));
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Null(dto.PlaceId);
        Assert.Equal("Cafe Central", dto.LocationLabel);
    }

    [Fact]
    public async Task Configured_geo_sets_place_id_and_canonical_label()
    {
        var geoId = Guid.NewGuid();
        using var scoped = Factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<IGeoResolver>(new StubGeo(geoId, "Cafe Central"))));
        var api = scoped.CreateClient();
        api.DefaultRequestHeaders.Add("X-Dev-User", Email);

        var calId = await CreateCalendarAsync(api);
        var resp = await api.PostAsJsonAsync("/items", Coffee(calId, "cafe central"));   // raw text differs from canonical
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal(geoId, dto.PlaceId);
        Assert.Equal("Cafe Central", dto.LocationLabel);
    }
}
