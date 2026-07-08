using LupiraCalApi.Dtos.CalendarItems;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>M6: structured fields are canonical — DAV regenerates ICS/vCard on demand, the ETag tracks canonical state, and DAV PUT parses into fields.</summary>
public sealed class CanonicalDavTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";
    static readonly DateTimeOffset Start = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Rest_edits_are_reflected_in_regenerated_dav_ics_with_a_new_etag()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var dav = Factory.DavClient(Email);

        var item = (await (await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = calId, Title = "Original", IsAllDay = false, StartsAt = Start, EndsAt = Start.AddHours(1), StartTimezone = "UTC",
        })).Content.ReadFromJsonAsync<CalendarItemDto>())!;
        var url = $"/dav/u/{uid}/cal/{calId}/{item.ExternalId}.ics";

        var first = await dav.GetAsync(url);
        Assert.Contains("SUMMARY:Original", await first.Content.ReadAsStringAsync());
        var etag1 = first.Headers.ETag?.Tag;

        (await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest { Title = "Renamed" })).EnsureSuccessStatusCode();

        var second = await dav.GetAsync(url);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("SUMMARY:Renamed", body);
        Assert.DoesNotContain("SUMMARY:Original", body);
        Assert.NotEqual(etag1, second.Headers.ETag?.Tag);   // ETag derives from canonical state
    }

    [Fact]
    public async Task Dav_put_parses_into_structured_fields_readable_over_rest()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var dav = Factory.DavClient(Email);

        var icalUid = "parsed@x";
        await SendDav(dav, "PUT", $"/dav/u/{uid}/cal/{calId}/{icalUid}.ics", body: MinimalIcs(icalUid, "Parsed", Start), contentType: "text/calendar");

        var from = Uri.EscapeDataString(Start.AddDays(-1).ToString("o"));
        var to = Uri.EscapeDataString(Start.AddDays(1).ToString("o"));
        var occ = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/items?calendarId={calId}&from={from}&to={to}");
        Assert.Contains(occ!, o => o.Title == "Parsed");   // the raw PUT was parsed into structured fields, not stored as a blob
    }
}
