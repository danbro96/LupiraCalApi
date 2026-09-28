using System.Net;
using System.Net.Http.Json;
using LupiraCalApi.Core.Abstractions;
using LupiraCalApi.Core.Dtos.CalendarItems;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Location resolution goes through <see cref="IGeoResolver"/> (LupiraGeoApi). REST/MCP require a pre-resolved
/// PlaceId (free text there is only a label); the free-text→place resolution lives on the CalDAV path, where external
/// clients cannot send a place id: geo configured ⇒ place id + canonical label; unconfigured/unreachable ⇒ label only.</summary>
public sealed class GeoResolutionTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private sealed class StubGeo(Guid id, string name) : IGeoResolver
    {
        public bool IsConfigured => true;
        public Task<GeoPlaceResolution?> ResolveAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<GeoPlaceResolution?>(new GeoPlaceResolution(id, name, 59.33, 18.06));
        public Task<IReadOnlyDictionary<Guid, GeoPlaceSummary>?> LookupAsync(IReadOnlyCollection<Guid> placeIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, GeoPlaceSummary>?>(null);
        public Task<GeoPlaceSummary?> NearestAsync(double latitude, double longitude, int radiusM, CancellationToken ct = default) =>
            Task.FromResult<GeoPlaceSummary?>(null);
        public Task<GeoReverseLabel?> ReverseAsync(double latitude, double longitude, CancellationToken ct = default) =>
            Task.FromResult<GeoReverseLabel?>(null);
    }

    // Geo configured but unreachable (GeocodeUnavailable / transport error) — the retryable case.
    private sealed class StubGeoDown : IGeoResolver
    {
        public bool IsConfigured => true;
        public Task<GeoPlaceResolution?> ResolveAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<GeoPlaceResolution?>(null);
        public Task<IReadOnlyDictionary<Guid, GeoPlaceSummary>?> LookupAsync(IReadOnlyCollection<Guid> placeIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, GeoPlaceSummary>?>(null);
        public Task<GeoPlaceSummary?> NearestAsync(double latitude, double longitude, int radiusM, CancellationToken ct = default) =>
            Task.FromResult<GeoPlaceSummary?>(null);
        public Task<GeoReverseLabel?> ReverseAsync(double latitude, double longitude, CancellationToken ct = default) =>
            Task.FromResult<GeoReverseLabel?>(null);
    }

    private HttpClient ClientWith(IGeoResolver geo)
    {
        var scoped = Factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton(geo)));
        var api = scoped.CreateClient();
        api.DefaultRequestHeaders.Add("X-Dev-User", Email);
        return api;
    }

    private static CreateCalendarItemRequest Coffee(Guid calId, string? location = null, Guid? placeId = null) => new()
    {
        CalendarId = calId,
        Title = "Coffee",
        IsAllDay = false,
        StartsAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
        EndsAt = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
        StartTimezone = "UTC",
        Location = location,
        PlaceId = placeId,
    };

    [Fact]
    public async Task Rest_create_rejects_free_text_location_without_a_place_id()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var resp = await api.PostAsJsonAsync("/items", Coffee(calId, location: "Cafe Central"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("PlaceId", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rest_create_with_place_id_attaches_it_and_keeps_location_as_label()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var placeId = Guid.NewGuid();
        var resp = await api.PostAsJsonAsync("/items", Coffee(calId, location: "Cafe Central", placeId: placeId));
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal(placeId, dto.PlaceId);
        Assert.Equal("Cafe Central", dto.LocationLabel);
    }

    [Fact]
    public async Task Rest_create_with_no_location_has_no_place()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var resp = await api.PostAsJsonAsync("/items", Coffee(calId));
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Null(dto.PlaceId);
        Assert.Null(dto.LocationLabel);
    }

    [Fact]
    public async Task Dav_put_configured_geo_sets_place_id_and_canonical_label()
    {
        var geoId = Guid.NewGuid();
        var api = ClientWith(new StubGeo(geoId, "Cafe Central"));
        var calId = await CreateCalendarAsync(api);
        var uid = $"{Guid.NewGuid():N}@test";
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//lupira-test//EN\r\nBEGIN:VEVENT\r\n" +
                  $"UID:{uid}\r\nSUMMARY:Coffee\r\nDTSTART:20260701T090000Z\r\nDTEND:20260701T100000Z\r\n" +
                  "LOCATION:cafe central\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        (await PutIcsBackendAsync(api, Email, calId, uid, ics)).EnsureSuccessStatusCode();

        var occ = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/items?calendarId={calId}&query=Coffee");
        var item = Assert.Single(occ!);
        Assert.Equal(geoId, item.PlaceId);
    }

    [Fact]
    public async Task Dav_put_geo_unreachable_still_stores_the_label()
    {
        var api = ClientWith(new StubGeoDown());
        var calId = await CreateCalendarAsync(api);
        var uid = $"{Guid.NewGuid():N}@test";
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//lupira-test//EN\r\nBEGIN:VEVENT\r\n" +
                  $"UID:{uid}\r\nSUMMARY:Coffee\r\nDTSTART:20260701T090000Z\r\nDTEND:20260701T100000Z\r\n" +
                  "LOCATION:Cafe Central\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var put = await PutIcsBackendAsync(api, Email, calId, uid, ics);
        put.EnsureSuccessStatusCode();   // DAV stays lenient — external free-text is never rejected

        var get = await GetIcsBackendAsync(api, Email, calId, uid);
        get.EnsureSuccessStatusCode();
        Assert.Contains("LOCATION:Cafe Central", await get.Content.ReadAsStringAsync());
    }

    // ---- Update-path place semantics (PlaceId + PlaceIdProvided sentinel) ----

    private async Task<CalendarItemDto> CreateWithPlaceAsync(HttpClient api, Guid calId, Guid placeId, string label = "Cafe Central")
    {
        var resp = await api.PostAsJsonAsync("/items", Coffee(calId, location: label, placeId: placeId));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    [Fact]
    public async Task Update_with_place_id_and_label_sets_both_without_geo()
    {
        var api = Factory.ApiClient(Email);   // geo unconfigured — a resolve attempt would label-only or fail
        var calId = await CreateCalendarAsync(api);
        var item = await CreateWithPlaceAsync(api, calId, Guid.NewGuid());

        var newPlace = Guid.NewGuid();
        var resp = await api.PutAsJsonAsync($"/items/{item.Id}",
            new UpdateCalendarItemRequest { PlaceId = newPlace, Location = "Cafe Nero" });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal(newPlace, dto.PlaceId);
        Assert.Equal("Cafe Nero", dto.LocationLabel);
    }

    [Fact]
    public async Task Update_with_provided_null_place_clears_it()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateWithPlaceAsync(api, calId, Guid.NewGuid());

        var resp = await api.PutAsJsonAsync($"/items/{item.Id}",
            new UpdateCalendarItemRequest { PlaceIdProvided = true, PlaceId = null, Location = "" });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Null(dto.PlaceId);
        Assert.Null(dto.LocationLabel);
    }

    [Fact]
    public async Task Update_with_changed_free_text_only_is_rejected()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateWithPlaceAsync(api, calId, Guid.NewGuid());

        var resp = await api.PutAsJsonAsync($"/items/{item.Id}",
            new UpdateCalendarItemRequest { Location = "Somewhere Else" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("PlaceId", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Update_replaying_the_same_label_keeps_the_place()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var placeId = Guid.NewGuid();
        var item = await CreateWithPlaceAsync(api, calId, placeId, "Cafe Central");

        // An offline client predating PlaceId replays the full core section with the unchanged label.
        var resp = await api.PutAsJsonAsync($"/items/{item.Id}",
            new UpdateCalendarItemRequest { Title = "Coffee v2", Location = "Cafe Central" });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal(placeId, dto.PlaceId);
        Assert.Equal("Cafe Central", dto.LocationLabel);
        Assert.Equal("Coffee v2", dto.Title);
    }
}
