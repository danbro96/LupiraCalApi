using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using LupiraCalApi.Dtos.CalendarItems;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class DavEdgeCaseTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task All_day_event_round_trips_and_is_found_by_time_range()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var dav = Factory.DavClient(Email);

        var icalUid = "allday@x";
        var url = $"/dav/u/{uid}/cal/{calId}/{icalUid}.ics";
        var ics = MinimalIcsAllDay(icalUid, "Holiday", new DateOnly(2026, 12, 24));

        Assert.Equal(HttpStatusCode.Created, (await SendDav(dav, "PUT", url, body: ics, contentType: "text/calendar")).StatusCode);
        var got = await (await dav.GetAsync(url)).Content.ReadAsStringAsync();   // GET regenerates from canonical fields (semantic round-trip)
        Assert.Contains($"UID:{icalUid}", got);
        Assert.Contains("SUMMARY:Holiday", got);
        Assert.Contains("20261224", got);   // all-day DTSTART preserved

        var doc = await ReadXml(await SendDav(dav, "REPORT", $"/dav/u/{uid}/cal/{calId}/",
            body: TimeRangeQueryBody(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero))));
        Assert.Contains(doc.Descendants(D + "href"), h => h.Value.Contains(icalUid));
    }

    [Fact]
    public async Task Dav_delete_removes_from_one_calendar_but_not_the_other()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var cal1 = await CreateCalendarAsync(api, "work", "Work");
        var cal2 = await CreateCalendarAsync(api, "family", "Family");
        var dav = Factory.DavClient(Email);

        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var create = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest { CalendarId = cal1, Title = "Shared", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC" });
        var item = (await create.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        (await api.PostAsync($"/items/{item.Id}/calendars/{cal2}/accept", null)).EnsureSuccessStatusCode();

        var url1 = $"/dav/u/{uid}/cal/{cal1}/{item.ExternalId}.ics";
        var url2 = $"/dav/u/{uid}/cal/{cal2}/{item.ExternalId}.ics";
        Assert.Equal(HttpStatusCode.OK, (await dav.GetAsync(url1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dav.GetAsync(url2)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendDav(dav, "DELETE", url1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await dav.GetAsync(url1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dav.GetAsync(url2)).StatusCode);   // still in cal2
    }
}
