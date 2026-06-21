using LupiraCalApi.Dtos.CalendarItems;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class AccessTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Other_user_cannot_read_a_private_item()
    {
        var a = Factory.ApiClient("a@x.test");
        var calId = await CreateCalendarAsync(a);
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var create = await a.PostAsJsonAsync("/items", new CreateCalendarItemRequest { CalendarId = calId, Title = "Secret", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC" });
        var item = (await create.Content.ReadFromJsonAsync<CalendarItemDto>())!;

        var b = Factory.ApiClient("b@x.test");
        var resp = await b.GetAsync($"/items/{item.Id}");
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

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        var anon = Factory.CreateClient();   // no auth header
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendDav(anon, "PROPFIND", "/dav/", depth: "0")).StatusCode);
    }

    [Fact]
    public async Task Other_user_cannot_read_a_private_contact()
    {
        var a = Factory.ApiClient("a@x.test");
        var abId = await CreateAddressBookAsync(a);
        var contact = await CreateContactAsync(a, abId);

        var b = Factory.ApiClient("b@x.test");
        var resp = await b.GetAsync($"/contacts/{contact.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
