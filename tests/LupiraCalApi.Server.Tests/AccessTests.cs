using LupiraCalApi.Dtos.CalendarItems;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.Server.Tests;

public sealed class AccessTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Other_user_cannot_read_a_private_item()
    {
        var a = Factory.ApiClient("a@x.test");
        var calId = await CreateCalendarAsync(a);
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var create = await a.PostAsJsonAsync("/api/items", new CreateCalendarItemRequest(calId, "Secret", null, null, null, false, start, start.AddHours(1), "UTC", null, null, null, null, null));
        var item = (await create.Content.ReadFromJsonAsync<CalendarItemDto>())!;

        var b = Factory.ApiClient("b@x.test");
        var resp = await b.GetAsync($"/api/items/{item.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Other_users_dav_calendar_home_omits_foreign_calendars()
    {
        var a = Factory.ApiClient("a@x.test");
        var calId = await CreateCalendarAsync(a);

        var bApi = Factory.ApiClient("b@x.test");
        var bId = await GetMyIdAsync(bApi);
        var bDav = Factory.DavClient("b@x.test");

        var doc = await ReadXml(await SendDav(bDav, "PROPFIND", $"/dav/u/{bId}/cal/", depth: "1"));
        Assert.DoesNotContain(doc.Descendants(D + "href"), h => h.Value.Contains(calId.ToString()));
    }

    [Fact]
    public async Task Cannot_address_another_principals_dav_tree()
    {
        var a = Factory.ApiClient("a@x.test");
        var aId = await GetMyIdAsync(a);
        var calId = await CreateCalendarAsync(a);

        var bDav = Factory.DavClient("b@x.test");
        var resp = await SendDav(bDav, "PROPFIND", $"/dav/u/{aId}/cal/{calId}/", depth: "1");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
