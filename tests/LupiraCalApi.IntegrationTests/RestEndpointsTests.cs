using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Calendars;
using LupiraCalApi.Dtos.Me;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class RestEndpointsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Me_returns_the_dev_user()
    {
        var api = Factory.ApiClient(Email);
        var me = await api.GetFromJsonAsync<MeDto>("/me");
        Assert.NotNull(me);
        Assert.Equal(Email, me!.Email);
    }

    [Fact]
    public async Task Created_calendar_is_listed_as_owner()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api, "work", "Work");

        var containers = await api.GetFromJsonAsync<List<ContainerDto>>("/calendars");
        var cal = Assert.Single(containers!, c => c.Id == calId);
        Assert.Equal("calendar", cal.Type);
        Assert.Equal(Access.Owner, cal.Access);
    }

    [Fact]
    public async Task Created_item_is_accepted_and_found_by_search()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

        var req = new CreateCalendarItemRequest { CalendarId = calId, Title = "Standup", Description = "daily", PlaceId = Guid.NewGuid(), Location = "Zoom", Status = "Confirmed", IsAllDay = false, StartsAt = start, EndsAt = start.AddMinutes(30), StartTimezone = "UTC", Category = "General", Tags = ["work"] };
        var create = await api.PostAsJsonAsync("/items", req);
        create.EnsureSuccessStatusCode();
        var dto = await create.Content.ReadFromJsonAsync<CalendarItemDto>();
        Assert.NotNull(dto);
        Assert.Contains(dto!.Calendars, m => m.CalendarId == calId && m.Status == CalendarEntryStatus.Accepted);

        var from = Uri.EscapeDataString(start.AddDays(-1).ToString("o"));
        var to = Uri.EscapeDataString(start.AddDays(1).ToString("o"));
        var occ = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/items?calendarId={calId}&from={from}&to={to}");
        Assert.Contains(occ!, o => o.Id == dto.Id);
    }

    [Fact]
    public async Task Bootstrap_creates_personal_containers_and_is_idempotent()
    {
        var api = Factory.ApiClient(Email);

        var first = await (await api.PostAsync("/me/bootstrap", null)).Content.ReadFromJsonAsync<List<ContainerDto>>();
        Assert.Contains(first!, c => c is { Type: "calendar", Slug: "personal" });

        var second = await (await api.PostAsync("/me/bootstrap", null)).Content.ReadFromJsonAsync<List<ContainerDto>>();
        Assert.Equal(first!.Select(c => c.Id).OrderBy(x => x), second!.Select(c => c.Id).OrderBy(x => x));

        var all = await api.GetFromJsonAsync<List<ContainerDto>>("/calendars");
        Assert.Equal(8, all!.Count);   // the 8 standard calendars — no duplicates
    }

    [Fact]
    public async Task Unfiled_item_has_no_calendar_memberships()
    {
        var api = Factory.ApiClient(Email);
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var req = new CreateCalendarItemRequest { Title = "Idea", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC" };
        var create = await api.PostAsJsonAsync("/items", req);
        create.EnsureSuccessStatusCode();
        var dto = await create.Content.ReadFromJsonAsync<CalendarItemDto>();
        Assert.Empty(dto!.Calendars);
    }
}
