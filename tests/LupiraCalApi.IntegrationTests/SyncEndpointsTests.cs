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
/// full-sync paging, section-guard exposure, Idempotency-Key replays, occurredAt LWW over REST, and the
/// totalized PUT (recurrence clear + all-day switch).</summary>
public class SyncEndpointsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    async Task<SyncChangesResponse> ChangesAsync(HttpClient api, string? since = null, int? limit = null)
    {
        var qs = new List<string>();
        if (since is not null) qs.Add($"since={since}");
        if (limit is not null) qs.Add($"limit={limit}");
        var url = "/sync/changes" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        var resp = await api.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SyncChangesResponse>(Json))!;
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
    public async Task Full_sync_pages_with_hasMore_and_covers_all_live_items()
    {
        var api = Factory.ApiClient("a@x");
        var cal = await CreateCalendarAsync(api);
        var live = new HashSet<Guid>();
        for (var n = 0; n < 3; n++) live.Add((await CreateItemAsync(api, cal, $"Item {n}")).Id);
        var doomed = await CreateItemAsync(api, cal, "Doomed");
        (await api.DeleteAsync($"/items/{doomed.Id}")).EnsureSuccessStatusCode();

        var seen = new HashSet<Guid>();
        string? cursor = null;
        var pages = 0;
        SyncChangesResponse page;
        do
        {
            page = await ChangesAsync(api, cursor, limit: 2);
            foreach (var c in page.Changed) seen.Add(c.Item.Id);
            Assert.True(page.Changed.Count <= 2);
            cursor = page.Cursor;
            pages++;
            Assert.True(pages < 20, "paging loop did not terminate");
        } while (page.HasMore);

        Assert.Equal(live, seen);
        Assert.DoesNotContain(doomed.Id, seen);
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
        Assert.Equal(full.Cursor, delta.Cursor);
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
        var resp = await Factory.ApiClient("a@x").GetAsync("/sync/changes?since=nope");
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
