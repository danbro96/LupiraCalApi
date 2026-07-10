using LupiraCalApi.Dtos.CalendarItems;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class CurationTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Proposed_item_is_invisible_on_the_dav_seam_until_accepted()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        // Create an unfiled item, then PROPOSE it into the calendar.
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var create = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest { Title = "Proposed", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC" });
        var item = (await create.Content.ReadFromJsonAsync<CalendarItemDto>())!;

        (await api.PostAsync($"/items/{item.Id}/calendars/{calId}?status=proposed", null)).EnsureSuccessStatusCode();

        // Proposed → not visible on the DAV seam.
        Assert.Equal(HttpStatusCode.NotFound, (await GetIcsBackendAsync(api, Email, calId, item.ExternalId)).StatusCode);
        Assert.DoesNotContain(item.ExternalId, await ListBackendUidsAsync(api, Email, calId));

        // Accept → now visible.
        (await api.PostAsync($"/items/{item.Id}/calendars/{calId}/accept", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await GetIcsBackendAsync(api, Email, calId, item.ExternalId)).StatusCode);
        Assert.Contains(item.ExternalId, await ListBackendUidsAsync(api, Email, calId));
    }

    [Fact]
    public async Task Item_accepted_into_two_calendars_is_visible_in_both()
    {
        var api = Factory.ApiClient(Email);
        var cal1 = await CreateCalendarAsync(api, "work", "Work");
        var cal2 = await CreateCalendarAsync(api, "family", "Family");

        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var create = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest { CalendarId = cal1, Title = "Shared", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC" });
        var item = (await create.Content.ReadFromJsonAsync<CalendarItemDto>())!;

        (await api.PostAsync($"/items/{item.Id}/calendars/{cal2}/accept", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await GetIcsBackendAsync(api, Email, cal1, item.ExternalId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetIcsBackendAsync(api, Email, cal2, item.ExternalId)).StatusCode);
    }

    [Fact]
    public async Task List_proposed_returns_proposed_items()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateUnfiledAsync(api);
        (await api.PostAsync($"/items/{item.Id}/calendars/{calId}?status=proposed", null)).EnsureSuccessStatusCode();

        var proposed = await api.GetFromJsonAsync<List<CalendarItemDto>>($"/calendars/{calId}/proposed");
        Assert.Contains(proposed!, i => i.Id == item.Id);
    }

    [Fact]
    public async Task Reject_removes_the_item_from_the_calendar()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var create = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest { CalendarId = calId, Title = "Filed", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC" });
        var item = (await create.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await GetIcsBackendAsync(api, Email, calId, item.ExternalId)).StatusCode);

        (await api.DeleteAsync($"/items/{item.Id}/calendars/{calId}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await GetIcsBackendAsync(api, Email, calId, item.ExternalId)).StatusCode);
    }

    [Fact]
    public async Task Add_with_accepted_status_is_immediately_visible()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateUnfiledAsync(api);

        (await api.PostAsync($"/items/{item.Id}/calendars/{calId}?status=accepted", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await GetIcsBackendAsync(api, Email, calId, item.ExternalId)).StatusCode);
    }

    private static async Task<CalendarItemDto> CreateUnfiledAsync(HttpClient api)
    {
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var create = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest { Title = "Unfiled", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC" });
        create.EnsureSuccessStatusCode();
        return (await create.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }
}
