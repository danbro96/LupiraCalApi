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

    private static async Task<CalendarItemDto> CreateAsync(HttpClient api, Guid calId, string title = "Mtg", string[]? tags = null, DateTimeOffset? startsAt = null, string? category = null, string? status = null, Guid? parentItemId = null)
    {
        var start = startsAt ?? new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest { CalendarId = calId, Title = title, IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC", Tags = tags, Category = category, Status = status, ParentItemId = parentItemId });
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
    public async Task Multi_day_all_day_occurrence_reports_inclusive_end()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = calId, Title = "Gbg trip", IsAllDay = true,
            StartDate = new DateOnly(2026, 7, 16), EndDate = new DateOnly(2026, 7, 18),
        });
        resp.EnsureSuccessStatusCode();

        var found = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>(
            $"/items?calendarId={calId}&from=2026-07-01T00:00:00Z&to=2026-07-31T00:00:00Z");
        var occ = Assert.Single(found!);
        Assert.True(occ.IsAllDay);
        Assert.Equal(new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero), occ.Start);
        // Inclusive last day at 00:00Z — the grid's coverage predicate spans 07-16..07-18 and stops at 07-19.
        Assert.Equal(new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero), occ.End);
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
    public async Task Search_enriches_parent_fields_and_child_count()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var trip = await CreateAsync(api, calId, "Tokyo trip", category: "Trip");
        await CreateAsync(api, calId, "Flight out", parentItemId: trip.Id);
        await CreateAsync(api, calId, "Hotel", parentItemId: trip.Id);

        var all = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items");
        var tripOcc = Assert.Single(all!, o => o.Title == "Tokyo trip");
        Assert.Null(tripOcc.ParentItemId);
        Assert.Equal(2, tripOcc.ChildCount);
        var leg = Assert.Single(all!, o => o.Title == "Flight out");
        Assert.Equal(trip.Id, leg.ParentItemId);
        Assert.Equal("Tokyo trip", leg.ParentTitle);
        Assert.Equal(0, leg.ChildCount);

        // ChildCount is independent of the current filters: a query excluding the legs keeps the count.
        var queried = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items?query=Tokyo");
        Assert.Equal(2, Assert.Single(queried!).ChildCount);
    }

    [Fact]
    public async Task Search_parent_title_gated_by_access()
    {
        var alice = Factory.ApiClient(Email);
        var privateId = await CreateCalendarAsync(alice, "private", "Private");
        var sharedId = await CreateCalendarAsync(alice, "shared", "Shared");
        var trip = await CreateAsync(alice, privateId, "Secret trip");
        await CreateAsync(alice, sharedId, "Flight out", parentItemId: trip.Id);
        (await alice.PostAsJsonAsync($"/calendars/{sharedId}/owners", new GrantOwnerRequest { Email = "bob@x.test", Access = "read" })).EnsureSuccessStatusCode();

        var bob = Factory.ApiClient("bob@x.test");
        var bobSees = await bob.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items");
        var leg = Assert.Single(bobSees!);
        Assert.Equal(trip.Id, leg.ParentItemId);
        Assert.Null(leg.ParentTitle);

        var aliceSees = await alice.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items?query=Flight");
        Assert.Equal("Secret trip", Assert.Single(aliceSees!).ParentTitle);
    }

    [Fact]
    public async Task Search_deleted_parent_yields_null_title()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var trip = await CreateAsync(api, calId, "Tokyo trip");
        await CreateAsync(api, calId, "Flight out", parentItemId: trip.Id);
        (await api.DeleteAsync($"/items/{trip.Id}")).EnsureSuccessStatusCode();

        var all = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items");
        var leg = Assert.Single(all!);
        Assert.Equal(trip.Id, leg.ParentItemId);
        Assert.Null(leg.ParentTitle);
    }

    [Fact]
    public async Task Search_child_count_counts_only_readable_children()
    {
        var alice = Factory.ApiClient(Email);
        var sharedId = await CreateCalendarAsync(alice, "shared", "Shared");
        var privateId = await CreateCalendarAsync(alice, "private", "Private");
        var trip = await CreateAsync(alice, sharedId, "Tokyo trip");
        await CreateAsync(alice, sharedId, "Flight out", parentItemId: trip.Id);
        await CreateAsync(alice, privateId, "Secret leg", parentItemId: trip.Id);
        (await alice.PostAsJsonAsync($"/calendars/{sharedId}/owners", new GrantOwnerRequest { Email = "bob@x.test", Access = "read" })).EnsureSuccessStatusCode();

        var bob = Factory.ApiClient("bob@x.test");
        var bobTrip = Assert.Single((await bob.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items?query=Tokyo"))!);
        Assert.Equal(1, bobTrip.ChildCount);
        var aliceTrip = Assert.Single((await alice.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items?query=Tokyo"))!);
        Assert.Equal(2, aliceTrip.ChildCount);
    }

    [Fact]
    public async Task Search_filters_by_parent_id()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var trip = await CreateAsync(api, calId, "Tokyo trip");
        var leg1 = await CreateAsync(api, calId, "Flight out", parentItemId: trip.Id);
        var leg2 = await CreateAsync(api, calId, "Hotel", parentItemId: trip.Id);
        await CreateAsync(api, calId, "Unrelated");

        var children = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/items?parentId={trip.Id}");
        Assert.Equal(2, children!.Count);
        Assert.Contains(children, o => o.Id == leg1.Id);
        Assert.Contains(children, o => o.Id == leg2.Id);
    }

    [Fact]
    public async Task Search_by_parent_id_defaults_to_all_time()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var trip = await CreateAsync(api, calId, "Old trip");
        await CreateAsync(api, calId, "Old flight", startsAt: new DateTimeOffset(2020, 3, 1, 8, 0, 0, TimeSpan.Zero), parentItemId: trip.Id);

        var children = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/items?parentId={trip.Id}");
        Assert.Contains(children!, o => o.Title == "Old flight");

        var browse = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items");
        Assert.DoesNotContain(browse!, o => o.Title == "Old flight");
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
