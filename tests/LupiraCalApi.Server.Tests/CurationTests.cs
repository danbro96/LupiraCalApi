using LupiraCalApi.Dtos.CalendarItems;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.Server.Tests;

public sealed class CurationTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Proposed_item_is_invisible_over_dav_until_accepted()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var dav = Factory.DavClient(Email);

        // Create an unfiled item, then PROPOSE it into the calendar.
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var create = await api.PostAsJsonAsync("/api/items", new CreateCalendarItemRequest(null, "Proposed", null, null, null, false, start, start.AddHours(1), "UTC", null, null, null, null, null));
        var item = (await create.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        var itemUrl = $"/dav/u/{uid}/cal/{calId}/{item.IcalUid}.ics";

        (await api.PostAsync($"/api/items/{item.Id}/calendars/{calId}?status=proposed", null)).EnsureSuccessStatusCode();

        // Proposed → not visible over DAV.
        Assert.Equal(HttpStatusCode.NotFound, (await dav.GetAsync(itemUrl)).StatusCode);
        var listed = await ReadXml(await SendDav(dav, "PROPFIND", $"/dav/u/{uid}/cal/{calId}/", depth: "1"));
        Assert.DoesNotContain(listed.Descendants(D + "href"), h => h.Value.Contains(item.IcalUid));

        // Accept → now visible.
        (await api.PostAsync($"/api/items/{item.Id}/calendars/{calId}/accept", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await dav.GetAsync(itemUrl)).StatusCode);
        var after = await ReadXml(await SendDav(dav, "PROPFIND", $"/dav/u/{uid}/cal/{calId}/", depth: "1"));
        Assert.Contains(after.Descendants(D + "href"), h => h.Value.Contains(item.IcalUid));
    }

    [Fact]
    public async Task Item_accepted_into_two_calendars_is_visible_in_both()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var cal1 = await CreateCalendarAsync(api, "work", "Work");
        var cal2 = await CreateCalendarAsync(api, "family", "Family");
        var dav = Factory.DavClient(Email);

        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var create = await api.PostAsJsonAsync("/api/items", new CreateCalendarItemRequest(cal1, "Shared", null, null, null, false, start, start.AddHours(1), "UTC", null, null, null, null, null));
        var item = (await create.Content.ReadFromJsonAsync<CalendarItemDto>())!;

        (await api.PostAsync($"/api/items/{item.Id}/calendars/{cal2}/accept", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await dav.GetAsync($"/dav/u/{uid}/cal/{cal1}/{item.IcalUid}.ics")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dav.GetAsync($"/dav/u/{uid}/cal/{cal2}/{item.IcalUid}.ics")).StatusCode);
    }
}
