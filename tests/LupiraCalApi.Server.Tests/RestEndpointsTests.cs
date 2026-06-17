using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Calendars;
using LupiraCalApi.Dtos.Me;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.Server.Tests;

public sealed class RestEndpointsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Me_returns_the_dev_user()
    {
        var api = Factory.ApiClient(Email);
        var me = await api.GetFromJsonAsync<MeDto>("/api/me");
        Assert.NotNull(me);
        Assert.Equal(Email, me!.Email);
    }

    [Fact]
    public async Task Created_calendar_is_listed_as_owner()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api, "work", "Work");

        var containers = await api.GetFromJsonAsync<List<ContainerDto>>("/api/calendars");
        var cal = Assert.Single(containers!, c => c.Id == calId);
        Assert.Equal("calendar", cal.Kind);
        Assert.Equal("Owner", cal.Access);
    }

    [Fact]
    public async Task Created_item_is_accepted_and_found_by_search()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

        var req = new CreateCalendarItemRequest(calId, "Standup", "daily", "Zoom", "Confirmed", false, start, start.AddMinutes(30), "UTC", null, null, null, "Generic", ["work"]);
        var create = await api.PostAsJsonAsync("/api/items", req);
        create.EnsureSuccessStatusCode();
        var dto = await create.Content.ReadFromJsonAsync<CalendarItemDto>();
        Assert.NotNull(dto);
        Assert.Contains(dto!.Calendars, m => m.CalendarId == calId && m.Status == "Accepted");

        var from = Uri.EscapeDataString(start.AddDays(-1).ToString("o"));
        var to = Uri.EscapeDataString(start.AddDays(1).ToString("o"));
        var occ = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/api/items?calendarId={calId}&from={from}&to={to}");
        Assert.Contains(occ!, o => o.Id == dto.Id);
    }

    [Fact]
    public async Task Unfiled_item_has_no_calendar_memberships()
    {
        var api = Factory.ApiClient(Email);
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var req = new CreateCalendarItemRequest(null, "Idea", null, null, null, false, start, start.AddHours(1), "UTC", null, null, null, null, null);
        var create = await api.PostAsJsonAsync("/api/items", req);
        create.EnsureSuccessStatusCode();
        var dto = await create.Content.ReadFromJsonAsync<CalendarItemDto>();
        Assert.Empty(dto!.Calendars);
    }
}
