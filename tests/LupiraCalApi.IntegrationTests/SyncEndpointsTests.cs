using Lupira.Sync;
using Lupira.Testing.Postgres;
using LupiraCalApi.Core.Dtos.Calendars;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Sync;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>The offline-client sync surface end to end: the delta loop (create → edit → unfile → delete),
/// full-sync paging, the /sync/changes alias, the calendars snapshot, section-guard exposure, Idempotency-Key
/// replays, occurredAt LWW over REST, and the totalized PUT (recurrence clear + all-day switch).</summary>
public class SyncEndpointsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    static string ItemsUrl(string path, string? since, int? limit)
    {
        var qs = new List<string>();
        if (since is not null) qs.Add($"since={since}");
        if (limit is not null) qs.Add($"limit={limit}");
        return path + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
    }

    async Task<SyncPage<ItemSyncChange>> ChangesAsync(HttpClient api, string? since = null, int? limit = null)
    {
        var resp = await api.GetAsync(ItemsUrl("/sync/items", since, limit));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SyncPage<ItemSyncChange>>(Json))!;
    }

    static async Task<CalendarItemDto> CreateItemAsync(HttpClient api, Guid calId, string title, string? sourceKey = null)
    {
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            Title = title,
            StartsAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
            CalendarId = calId,
            SourceKey = sourceKey,
        }, Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>(Json))!;
    }

    [Fact]
    public async Task Delta_loop_sees_create_edit_unfile_and_delete()
    {
        var api = Factory.ApiClient("a@x");
        var cal = await CreateCalendarAsync(api);

        var start = await ChangesAsync(api);
        Assert.False(start.HasMore);

        // create → surfaces as changed, with stamps + guards
        var item = await CreateItemAsync(api, cal, "Lunch");
        var afterCreate = await ChangesAsync(api, start.Cursor);
        var entry = Assert.Single(afterCreate.Changed, c => c.Item.Id == item.Id);
        Assert.True(entry.Item.Version >= 1);
        Assert.True(entry.Item.UpdatedAt >= entry.Item.CreatedAt);
        Assert.NotEqual(default, entry.Guards.Core.Ts);
        Assert.True(entry.Guards.Filing.ContainsKey(cal));
        Assert.Empty(afterCreate.Deleted);

        // edit → changed again past the new cursor
        var put = await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest { Title = "Lunch v2" }, Json);
        put.EnsureSuccessStatusCode();
        var afterEdit = await ChangesAsync(api, afterCreate.Cursor);
        Assert.Equal("Lunch v2", Assert.Single(afterEdit.Changed, c => c.Item.Id == item.Id).Item.Title);

        // unfile from its only calendar → visibility lost → tombstone
        var unfile = await api.DeleteAsync($"/items/{item.Id}/calendars/{cal}");
        unfile.EnsureSuccessStatusCode();
        var afterUnfile = await ChangesAsync(api, afterEdit.Cursor);
        Assert.Contains(item.Id, afterUnfile.Deleted);
        Assert.DoesNotContain(afterUnfile.Changed, c => c.Item.Id == item.Id);

        // refile + soft delete → tombstone again
        (await api.PostAsync($"/items/{item.Id}/calendars/{cal}?status=accepted", null)).EnsureSuccessStatusCode();
        (await api.DeleteAsync($"/items/{item.Id}")).EnsureSuccessStatusCode();
        var afterDelete = await ChangesAsync(api, afterUnfile.Cursor);
        Assert.Contains(item.Id, afterDelete.Deleted);

        // quiet feed: cursor is stable and yields nothing
        var quiet = await ChangesAsync(api, afterDelete.Cursor);
        Assert.Empty(quiet.Changed);
        Assert.Empty(quiet.Deleted);
        Assert.Equal(afterDelete.Cursor, quiet.Cursor);
    }

    [Fact]
    public async Task Full_sync_pages_by_id_then_hands_over_to_a_delta()
    {
        var api = Factory.ApiClient("a@x");
        var cal = await CreateCalendarAsync(api);
        var live = new HashSet<Guid>();
        for (var n = 0; n < 5; n++) live.Add((await CreateItemAsync(api, cal, $"Item {n}")).Id);
        var doomed = await CreateItemAsync(api, cal, "Doomed");
        (await api.DeleteAsync($"/items/{doomed.Id}")).EnsureSuccessStatusCode();

        var seen = new List<Guid>();
        var pages = new List<SyncPage<ItemSyncChange>>();
        string? cursor = null;
        SyncPage<ItemSyncChange> page;
        do
        {
            page = await ChangesAsync(api, cursor, limit: 2);
            pages.Add(page);
            seen.AddRange(page.Changed.Select(c => c.Item.Id));
            Assert.True(page.Changed.Count <= 2);
            Assert.Empty(page.Deleted);
            cursor = page.Cursor;
            Assert.True(pages.Count < 20, "paging loop did not terminate");
        } while (page.HasMore);

        Assert.Equal(3, pages.Count);
        Assert.True(pages[0].Reset);
        Assert.All(pages.Skip(1), p => Assert.False(p.Reset));
        Assert.Equal(live.Count, seen.Count);
        Assert.Equal(live, seen.ToHashSet());
        Assert.True(SyncCursor.TryParse(pages[0].Cursor, out var firstCursor) && firstCursor.After is not null);
        Assert.True(SyncCursor.TryParse(page.Cursor, out var lastCursor) && lastCursor.After is null);
        Assert.Equal(firstCursor.Sequence, lastCursor.Sequence);

        var edited = live.First();
        (await api.PutAsJsonAsync($"/items/{edited}", new UpdateCalendarItemRequest { Title = "Edited" }, Json)).EnsureSuccessStatusCode();
        var delta = await ChangesAsync(api, page.Cursor);
        Assert.False(delta.Reset);
        Assert.Equal("Edited", Assert.Single(delta.Changed).Item.Title);
        Assert.Empty(delta.Deleted);
    }

    [Fact]
    public async Task Delta_pages_across_more_changes_than_the_limit()
    {
        var api = Factory.ApiClient("a@x");
        var cal = await CreateCalendarAsync(api);
        var start = await ChangesAsync(api);
        var created = new HashSet<Guid>();
        for (var n = 0; n < 5; n++) created.Add((await CreateItemAsync(api, cal, $"Item {n}")).Id);

        var seen = new List<Guid>();
        var cursor = start.Cursor;
        SyncPage<ItemSyncChange> page;
        var pages = 0;
        do
        {
            page = await ChangesAsync(api, cursor, limit: 2);
            Assert.False(page.Reset);
            seen.AddRange(page.Changed.Select(c => c.Item.Id));
            cursor = page.Cursor;
            Assert.True(++pages < 20, "paging loop did not terminate");
        } while (page.HasMore);

        Assert.Equal(3, pages);
        Assert.Equal(created.Count, seen.Count);
        Assert.Equal(created, seen.ToHashSet());
    }

    [Fact]
    public async Task Demoting_the_only_filing_to_proposed_tombstones_the_item()
    {
        var api = Factory.ApiClient("a@x");
        var cal = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, cal, "Lunch");
        var full = await ChangesAsync(api);
        Assert.Contains(full.Changed, c => c.Item.Id == item.Id);

        (await api.PostAsync($"/items/{item.Id}/calendars/{cal}?status=proposed", null)).EnsureSuccessStatusCode();
        var delta = await ChangesAsync(api, full.Cursor);
        Assert.Equal([item.Id], delta.Deleted);
        Assert.Empty(delta.Changed);
    }

    [Fact]
    public async Task Changes_alias_answers_exactly_like_items()
    {
        var api = Factory.ApiClient("a@x");
        var cal = await CreateCalendarAsync(api);
        await CreateItemAsync(api, cal, "One");
        await CreateItemAsync(api, cal, "Two");

        var items = await api.GetStringAsync(ItemsUrl("/sync/items", null, 1));
        var alias = await api.GetStringAsync(ItemsUrl("/sync/changes", null, 1));
        Assert.Equal(items, alias);

        var cursor = JsonDocument.Parse(items).RootElement.GetProperty("cursor").GetString();
        Assert.Equal(await api.GetStringAsync(ItemsUrl("/sync/items", cursor, null)), await api.GetStringAsync(ItemsUrl("/sync/changes", cursor, null)));
    }

    [Fact]
    public async Task Items_invisible_to_other_principals_never_leak_content()
    {
        var api = Factory.ApiClient("a@x");
        var stranger = Factory.ApiClient("b@x");
        var cal = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, cal, "Private");

        var theirView = await ChangesAsync(stranger);
        Assert.DoesNotContain(theirView.Changed, c => c.Item.Id == item.Id);
    }

    [Fact]
    public async Task Another_callers_churn_is_neither_content_nor_tombstones()
    {
        var api = Factory.ApiClient("a@x");
        var stranger = Factory.ApiClient("b@x");
        var cal = await CreateCalendarAsync(api);
        await CreateItemAsync(api, cal, "Before");

        var full = await ChangesAsync(stranger);
        Assert.Empty(full.Changed);
        Assert.False(full.HasMore);

        var item = await CreateItemAsync(api, cal, "After");
        (await api.DeleteAsync($"/items/{item.Id}")).EnsureSuccessStatusCode();
        var delta = await ChangesAsync(stranger, full.Cursor);
        Assert.False(delta.Reset);
        Assert.Empty(delta.Changed);
        Assert.Empty(delta.Deleted);
    }

    [Fact]
    public async Task A_grant_restarts_the_stream_so_the_shared_calendar_arrives()
    {
        var api = Factory.ApiClient("a@x");
        var partner = Factory.ApiClient("b@x");
        var cal = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, cal, "Shared");

        var before = await ChangesAsync(partner);
        Assert.True(before.Reset);
        Assert.Empty(before.Changed);

        (await api.PostAsJsonAsync($"/calendars/{cal}/owners", new GrantOwnerRequest { Email = "b@x", Access = "owner" })).EnsureSuccessStatusCode();
        var afterGrant = await ChangesAsync(partner, before.Cursor);
        Assert.True(afterGrant.Reset);
        Assert.Contains(afterGrant.Changed, c => c.Item.Id == item.Id);

        var settled = await ChangesAsync(partner, afterGrant.Cursor);
        Assert.False(settled.Reset);
        Assert.Empty(settled.Changed);

        (await api.DeleteAsync($"/calendars/{cal}/owners?email=b@x")).EnsureSuccessStatusCode();
        var afterRevoke = await ChangesAsync(partner, settled.Cursor);
        Assert.True(afterRevoke.Reset);
        Assert.DoesNotContain(afterRevoke.Changed, c => c.Item.Id == item.Id);
    }

    [Fact]
    public async Task A_bare_sequence_cursor_restarts_the_stream_once()
    {
        var api = Factory.ApiClient("a@x");
        var cal = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, cal, "Lunch");

        var legacy = await ChangesAsync(api, "999999999");
        Assert.True(legacy.Reset);
        Assert.Contains(legacy.Changed, c => c.Item.Id == item.Id);
        Assert.False((await ChangesAsync(api, legacy.Cursor)).Reset);
    }

    [Fact]
    public async Task A_garbage_cursor_is_rejected()
    {
        var resp = await Factory.ApiClient("a@x").GetAsync("/sync/items?since=nope");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Replayed_update_with_same_idempotency_key_does_not_reapply()
    {
        var api = Factory.ApiClient("a@x");
        var cal = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, cal, "Original");
        var key = Guid.NewGuid();

        using var first = new HttpRequestMessage(HttpMethod.Put, $"/items/{item.Id}")
        { Content = JsonContent.Create(new UpdateCalendarItemRequest { Title = "Applied" }, options: Json) };
        first.Headers.Add("Idempotency-Key", key.ToString());
        (await api.SendAsync(first)).EnsureSuccessStatusCode();

        using var replay = new HttpRequestMessage(HttpMethod.Put, $"/items/{item.Id}")
        { Content = JsonContent.Create(new UpdateCalendarItemRequest { Title = "Should not apply" }, options: Json) };
        replay.Headers.Add("Idempotency-Key", key.ToString());
        var replayResp = await api.SendAsync(replay);
        replayResp.EnsureSuccessStatusCode();
        var replayDto = (await replayResp.Content.ReadFromJsonAsync<CalendarItemDto>(Json))!;

        Assert.Equal("Applied", replayDto.Title);
        var current = (await api.GetFromJsonAsync<CalendarItemDto>($"/items/{item.Id}", Json))!;
        Assert.Equal("Applied", current.Title);
    }

    [Fact]
    public async Task Non_guid_idempotency_key_is_a_problem_naming_the_header()
    {
        var api = Factory.ApiClient("a@x");
        var cal = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, cal, "Original");

        using var req = new HttpRequestMessage(HttpMethod.Put, $"/items/{item.Id}")
        { Content = JsonContent.Create(new UpdateCalendarItemRequest { Title = "Applied" }, options: Json) };
        req.Headers.Add("Idempotency-Key", "not-a-guid");
        var resp = await api.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("Idempotency-Key must be a GUID.", problem.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Replayed_delete_with_same_idempotency_key_succeeds_instead_of_404()
    {
        var api = Factory.ApiClient("a@x");
        var cal = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, cal, "Doomed");
        var key = Guid.NewGuid();

        using var first = new HttpRequestMessage(HttpMethod.Delete, $"/items/{item.Id}");
        first.Headers.Add("Idempotency-Key", key.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await api.SendAsync(first)).StatusCode);

        using var replay = new HttpRequestMessage(HttpMethod.Delete, $"/items/{item.Id}");
        replay.Headers.Add("Idempotency-Key", key.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await api.SendAsync(replay)).StatusCode);

        // without the key the second delete is a plain 404
        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteAsync($"/items/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task Stale_occurredAt_update_loses_to_a_newer_write()
    {
        var api = Factory.ApiClient("a@x");
        var cal = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, cal, "Original");
        var t = DateTimeOffset.UtcNow;

        (await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest { Title = "Newer", OccurredAt = t.AddMinutes(10) }, Json)).EnsureSuccessStatusCode();
        (await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest { Title = "Stale offline edit", OccurredAt = t.AddMinutes(5) }, Json)).EnsureSuccessStatusCode();

        var current = (await api.GetFromJsonAsync<CalendarItemDto>($"/items/{item.Id}", Json))!;
        Assert.Equal("Newer", current.Title);
    }

    [Fact]
    public async Task Totalized_put_clears_recurrence_and_switches_all_day()
    {
        var api = Factory.ApiClient("a@x");
        var cal = await CreateCalendarAsync(api);
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            Title = "Weekly",
            StartsAt = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero),
            RecurrenceRule = "FREQ=WEEKLY",
            CalendarId = cal,
        }, Json);
        resp.EnsureSuccessStatusCode();
        var item = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>(Json))!;
        Assert.Equal("FREQ=WEEKLY", item.RecurrenceRule);

        var put = await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest
        {
            RecurrenceRule = null,
            RecurrenceRuleProvided = true,
            IsAllDay = true,
            StartDate = new DateOnly(2026, 8, 3),
            StartDateProvided = true,
            EndDate = new DateOnly(2026, 8, 4),
            EndDateProvided = true,
            StartsAt = null,
            StartsAtProvided = true,
            EndsAt = null,
            EndsAtProvided = true,
        }, Json);
        put.EnsureSuccessStatusCode();

        var updated = (await api.GetFromJsonAsync<CalendarItemDto>($"/items/{item.Id}", Json))!;
        Assert.Null(updated.RecurrenceRule);
        Assert.True(updated.IsAllDay);
        Assert.Equal(new DateOnly(2026, 8, 3), updated.StartDate);
        Assert.Null(updated.StartsAt);
    }

    [Fact]
    public async Task Calendars_snapshot_is_always_a_reset_page()
    {
        var api = Factory.ApiClient("a@x");
        var cal = await CreateCalendarAsync(api);
        var resp = await api.GetAsync("/sync/calendars");
        resp.EnsureSuccessStatusCode();
        var body = (await resp.Content.ReadFromJsonAsync<SyncPage<ContainerDto>>(Json))!;
        Assert.True(body.Reset);
        Assert.False(body.HasMore);
        Assert.Equal("", body.Cursor);
        Assert.Empty(body.Deleted);
        Assert.Equal(cal, Assert.Single(body.Changed).Id);
    }

    [Fact]
    public async Task Containers_snapshot_lists_the_callers_calendars()
    {
        var api = Factory.ApiClient("a@x");
        var cal = await CreateCalendarAsync(api);
        var resp = await api.GetAsync("/sync/containers");
        resp.EnsureSuccessStatusCode();
        var body = (await resp.Content.ReadFromJsonAsync<List<ContainerDto>>(Json))!;
        Assert.Contains(body, c => c.Id == cal);
    }
}
