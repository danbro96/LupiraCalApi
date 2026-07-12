using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Calendars;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class CalendarItemsRestTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static async Task<CalendarItemDto> CreateAsync(HttpClient api, Guid calId, string title = "Mtg", string[]? tags = null, DateTimeOffset? startsAt = null, string? category = null, string? status = null)
    {
        var start = startsAt ?? new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest { CalendarId = calId, Title = title, IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC", Tags = tags, Category = category, Status = status });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    [Fact]
    public async Task Update_changes_fields()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateAsync(api, calId);

        var upd = await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest { Title = "Renamed", Description = "new desc" });
        upd.EnsureSuccessStatusCode();

        var got = await api.GetFromJsonAsync<CalendarItemDto>($"/items/{item.Id}");
        Assert.Equal("Renamed", got!.Title);
        Assert.Equal("new desc", got.Description);
    }

    [Fact]
    public async Task Delete_then_get_is_not_found()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateAsync(api, calId);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/items/{item.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/items/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task Metadata_is_merged()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateAsync(api, calId);

        JsonNode patch = new JsonObject { ["trip"] = "tokyo-2026" };
        var resp = await api.PostAsJsonAsync($"/items/{item.Id}/metadata", patch);
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal("tokyo-2026", dto.Metadata?["trip"]?.GetValue<string>());
    }

    [Fact]
    public async Task Search_filters_by_tag_and_text()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        await CreateAsync(api, calId, "Standup", ["work"]);
        await CreateAsync(api, calId, "Dentist", ["health"]);

        var from = Uri.EscapeDataString(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).ToString("o"));
        var to = Uri.EscapeDataString(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero).ToString("o"));

        var byTag = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/items?tag=work&from={from}&to={to}");
        Assert.Contains(byTag!, o => o.Title == "Standup");
        Assert.DoesNotContain(byTag!, o => o.Title == "Dentist");

        var byText = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/items?query=Dentist&from={from}&to={to}");
        Assert.Contains(byText!, o => o.Title == "Dentist");
        Assert.DoesNotContain(byText!, o => o.Title == "Standup");
    }

    [Fact]
    public async Task Search_filters_by_category_and_status()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        await CreateAsync(api, calId, "Standup", category: "Meeting", status: "Confirmed");
        await CreateAsync(api, calId, "Dentist", category: "Appointment", status: "Tentative");

        var byCategory = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items?category=meeting");
        Assert.Equal(["Standup"], byCategory!.Select(o => o.Title));

        var byStatus = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items?status=Tentative");
        Assert.Equal(["Dentist"], byStatus!.Select(o => o.Title));

        var combined = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items?category=Meeting&status=Tentative");
        Assert.Empty(combined!);
    }

    [Fact]
    public async Task Search_rejects_unknown_category_status_and_bad_paging()
    {
        var api = Factory.ApiClient(Email);

        var badCategory = await api.GetAsync("/items?category=Bogus");
        Assert.Equal(HttpStatusCode.BadRequest, badCategory.StatusCode);
        Assert.Contains("Valid values", await badCategory.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/items?status=Maybe")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/items?skip=-1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/items?take=0")).StatusCode);
    }

    [Fact]
    public async Task Search_pages_and_sorts_occurrences()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var day1 = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        await CreateAsync(api, calId, "First", startsAt: day1);
        await CreateAsync(api, calId, "Second", startsAt: day1.AddDays(1));
        await CreateAsync(api, calId, "Third", startsAt: day1.AddDays(2));

        var firstPage = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items?take=2");
        Assert.Equal(["First", "Second"], firstPage!.Select(o => o.Title));

        var secondPage = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items?skip=2&take=2");
        Assert.Equal(["Third"], secondPage!.Select(o => o.Title));

        var newestFirst = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items?desc=true&take=2");
        Assert.Equal(["Third", "Second"], newestFirst!.Select(o => o.Title));
    }

    [Fact]
    public async Task Occurrences_carry_calendar_ids_category_status_and_tags()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        await CreateAsync(api, calId, "Standup", tags: ["work"], category: "Meeting", status: "Confirmed");

        var found = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items?query=Standup");
        var occ = Assert.Single(found!);
        Assert.Equal([calId], occ.CalendarIds);
        Assert.Equal(ItemCategory.Meeting, occ.Category);
        Assert.Equal(ItemStatus.Confirmed, occ.Status);
        Assert.Equal(["work"], occ.Tags!);
    }

    [Fact]
    public async Task Calendar_ids_report_all_readable_memberships_and_never_unreadable_ones()
    {
        var alice = Factory.ApiClient(Email);
        var workId = await CreateCalendarAsync(alice, "work", "Work");
        var famId = await CreateCalendarAsync(alice, "fam", "Family");
        var item = await CreateAsync(alice, workId, "Standup");
        (await alice.PostAsync($"/items/{item.Id}/calendars/{famId}?status=accepted", null)).EnsureSuccessStatusCode();
        (await alice.PostAsJsonAsync($"/calendars/{workId}/owners", new GrantOwnerRequest { Email = "bob@x.test", Access = "read" })).EnsureSuccessStatusCode();

        // Narrowing to one calendar must not under-report the caller's other readable memberships.
        var narrowed = await alice.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/items?calendarId={workId}");
        var ids = Assert.Single(narrowed!).CalendarIds;
        Assert.Equal(2, ids.Length);
        Assert.Contains(workId, ids);
        Assert.Contains(famId, ids);

        var bob = Factory.ApiClient("bob@x.test");
        var bobSees = await bob.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items?query=Standup");
        Assert.Equal([workId], Assert.Single(bobSees!).CalendarIds);
    }

    [Fact]
    public async Task Query_without_window_matches_all_time_but_browse_keeps_the_default_window()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        await CreateAsync(api, calId, "Konfirmation", startsAt: new DateTimeOffset(2011, 6, 29, 12, 0, 0, TimeSpan.Zero));

        var byQuery = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items?query=konfirmation");
        Assert.Contains(byQuery!, o => o.Title == "Konfirmation");

        var browse = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items");
        Assert.DoesNotContain(browse!, o => o.Title == "Konfirmation");
    }
}
