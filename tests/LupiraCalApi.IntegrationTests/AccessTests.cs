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
    public async Task Other_users_dav_collections_omit_foreign_calendars()
    {
        var a = Factory.ApiClient("a@x.test");
        var calId = await CreateCalendarAsync(a);

        var b = Factory.ApiClient("b@x.test");
        var resp = await b.GetAsync($"{DavBackendBase("b@x.test")}/collections");
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<LupiraCalApi.Dav.DavCollectionsDto>();
        Assert.DoesNotContain(dto!.Collections, c => c.Id == calId);
    }

    [Fact]
    public async Task Foreign_calendars_are_an_opaque_404_on_the_dav_seam()
    {
        var a = Factory.ApiClient("a@x.test");
        var calId = await CreateCalendarAsync(a);

        var b = Factory.ApiClient("b@x.test");
        var resp = await b.PostAsJsonAsync($"{DavBackendBase("b@x.test")}/collections/{calId}/query", new LupiraCalApi.Dav.DavQueryRequest());
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        var anon = Factory.CreateClient();   // no auth header
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"{DavBackendBase("a@x.test")}/collections")).StatusCode);
    }
}
