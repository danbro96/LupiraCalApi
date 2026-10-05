using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.CalendarItems.Events;
using LupiraCalApi.Core.Dtos.CalendarItems;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>
/// Regression tests for the broken-object-level-authorization bugs found in security reviews:
/// (1) curation IDOR — filing a foreign item into your own calendar to self-grant access;
/// (2) DAV cross-tenant overwrite/delete by UID — item/contact streams are keyed by UID alone, so knowing a
/// victim's iCal/vCard UID let an attacker overwrite, re-file, or delete the victim's resource; and
/// (3) create by SourceKey — the key pins the stream id, so a known key returned or took over the victim's item.
/// </summary>
public sealed class SecurityRegressionTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    static readonly DateTimeOffset Start = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    static CreateCalendarItemRequest Event(Guid? calId, string title) =>
        new() { CalendarId = calId, Title = title, IsAllDay = false, StartsAt = Start, EndsAt = Start.AddHours(1), StartTimezone = "UTC" };

    static CreateCalendarItemRequest Keyed(Guid? calId, string title, string sourceKey)
    {
        var r = Event(calId, title);
        r.SourceKey = sourceKey;
        return r;
    }

    // ---------- Vuln 1: curation IDOR (CurationService) ----------

    [Fact]
    public async Task AddToCalendar_cannot_file_another_users_item()
    {
        var a = Factory.ApiClient("a@x.test");
        var calA = await CreateCalendarAsync(a, "a-cal");
        var item = (await (await a.PostAsJsonAsync("/items", Event(calA, "A secret"))).Content.ReadFromJsonAsync<CalendarItemDto>())!;

        var b = Factory.ApiClient("b@x.test");
        var calB = await CreateCalendarAsync(b, "b-cal");

        // B tries to file A's item into B's own calendar to self-grant read/write access.
        var add = await b.PostAsync($"/items/{item.Id}/calendars/{calB}?status=accepted", null);
        Assert.Equal(HttpStatusCode.NotFound, add.StatusCode);

        // B still cannot read A's item.
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync($"/items/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task Accept_cannot_accept_item_not_proposed_into_my_calendar()
    {
        var a = Factory.ApiClient("a@x.test");
        var calA = await CreateCalendarAsync(a, "a-cal");
        var item = (await (await a.PostAsJsonAsync("/items", Event(calA, "A secret"))).Content.ReadFromJsonAsync<CalendarItemDto>())!;

        var b = Factory.ApiClient("b@x.test");
        var calB = await CreateCalendarAsync(b, "b-cal");

        var accept = await b.PostAsync($"/items/{item.Id}/calendars/{calB}/accept", null);
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
    }

    [Fact] // no-regression: filing your own unfiled item must still work
    public async Task AddToCalendar_files_my_own_unfiled_item()
    {
        var a = Factory.ApiClient("a@x.test");
        var calA = await CreateCalendarAsync(a, "a-cal");
        var item = (await (await a.PostAsJsonAsync("/items", Event(null, "Unfiled"))).Content.ReadFromJsonAsync<CalendarItemDto>())!;

        var add = await a.PostAsync($"/items/{item.Id}/calendars/{calA}?status=accepted", null);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync($"/items/{item.Id}")).StatusCode);
    }

    // ---------- Vuln 2: DAV cross-tenant by UID ----------

    [Fact]
    public async Task Dav_put_cannot_overwrite_another_users_item_by_uid()
    {
        const string uid = "evt-shared@x";
        var a = Factory.ApiClient("a@x.test");
        var calA = await CreateCalendarAsync(a, "a-cal");
        var aIcs = MinimalIcs(uid, "A meeting", Start);
        Assert.Equal(HttpStatusCode.Created, (await PutIcsBackendAsync(a, "a@x.test", calA, uid, aIcs)).StatusCode);

        var b = Factory.ApiClient("b@x.test");
        var calB = await CreateCalendarAsync(b, "b-cal");
        var bIcs = MinimalIcs(uid, "B hijack", Start.AddDays(1));

        // B PUTs the same UID into B's own calendar — must not touch A's item.
        Assert.Equal(HttpStatusCode.Forbidden, (await PutIcsBackendAsync(b, "b@x.test", calB, uid, bIcs)).StatusCode);

        // A's item is unchanged — still A's, not B's hijack attempt.
        var get = await GetIcsBackendAsync(a, "a@x.test", calA, uid);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var got = await get.Content.ReadAsStringAsync();
        Assert.Contains("SUMMARY:A meeting", got);
        Assert.DoesNotContain("B hijack", got);
    }

    // ---------- Vuln 3: create by SourceKey ----------

    const string Key = "invite-uid-123@x";

    [Fact]
    public async Task Create_with_another_users_source_key_is_forbidden_without_leaking_the_item()
    {
        var a = Factory.ApiClient("a@x.test");
        var calA = await CreateCalendarAsync(a, "a-cal");
        var item = (await (await a.PostAsJsonAsync("/items", Keyed(calA, "A secret", Key))).Content.ReadFromJsonAsync<CalendarItemDto>())!;

        var b = Factory.ApiClient("b@x.test");
        var calB = await CreateCalendarAsync(b, "b-cal");
        foreach (var target in new Guid?[] { calB, null })
        {
            var resp = await b.PostAsJsonAsync("/items", Keyed(target, "B probe", Key));
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("source key belongs to an item you can't access", body);
            Assert.DoesNotContain("A secret", body);
            Assert.DoesNotContain(item.Id.ToString(), body);
        }

        var replay = await a.PostAsJsonAsync("/items", Keyed(calA, "A secret", Key));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var again = (await replay.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal(item.Id, again.Id);
        Assert.Equal("A secret", again.Title);
    }

    [Fact]
    public async Task Batch_with_another_users_source_key_rejects_that_entry_without_its_id()
    {
        var a = Factory.ApiClient("a@x.test");
        var calA = await CreateCalendarAsync(a, "a-cal");
        var item = (await (await a.PostAsJsonAsync("/items", Keyed(calA, "A secret", Key))).Content.ReadFromJsonAsync<CalendarItemDto>())!;

        var b = Factory.ApiClient("b@x.test");
        var calB = await CreateCalendarAsync(b, "b-cal");
        var resp = await b.PostAsJsonAsync("/items/batch", new CreateCalendarItemsBatchRequest { Items = [Keyed(calB, "B probe", Key), Keyed(calB, "B own", "b-own")] });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(item.Id.ToString(), body);
        Assert.DoesNotContain("A secret", body);

        var res = (await resp.Content.ReadFromJsonAsync<List<ItemBatchResult>>())!;
        Assert.Equal("forbidden", res[0].Status);
        Assert.Null(res[0].ItemId);
        Assert.Equal("created", res[1].Status);

        var replay = (await (await a.PostAsJsonAsync("/items/batch", new CreateCalendarItemsBatchRequest { Items = [Keyed(calA, "A secret", Key)] }))
            .Content.ReadFromJsonAsync<List<ItemBatchResult>>())!;
        Assert.Equal("existed", replay[0].Status);
        Assert.Equal(item.Id, replay[0].ItemId);
    }

    [Fact]
    public async Task Deleted_item_source_key_is_reusable_only_with_write_access_to_its_calendar()
    {
        var a = Factory.ApiClient("a@x.test");
        var calA = await CreateCalendarAsync(a, "a-cal");
        var item = (await (await a.PostAsJsonAsync("/items", Keyed(calA, "A secret", Key))).Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/items/{item.Id}")).StatusCode);

        var b = Factory.ApiClient("b@x.test");
        var calB = await CreateCalendarAsync(b, "b-cal");
        var takeover = await b.PostAsJsonAsync("/items", Keyed(calB, "B takeover", Key));
        Assert.Equal(HttpStatusCode.Forbidden, takeover.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/items/{item.Id}")).StatusCode);

        var recreate = await a.PostAsJsonAsync("/items", Keyed(calA, "A again", Key));
        Assert.Equal(HttpStatusCode.OK, recreate.StatusCode);
        Assert.Equal(item.Id, (await recreate.Content.ReadFromJsonAsync<CalendarItemDto>())!.Id);
        var restored = await a.GetFromJsonAsync<CalendarItemDto>($"/items/{item.Id}");
        Assert.Equal("A again", restored!.Title);
    }

    [Fact]
    public async Task Mcp_create_with_another_users_source_key_is_a_tool_error()
    {
        var a = Factory.ApiClient("a@x.test");
        var calA = await CreateCalendarAsync(a, "a-cal");
        (await a.PostAsJsonAsync("/items", Keyed(calA, "A secret", Key))).EnsureSuccessStatusCode();

        var http = Factory.ApiClient("b@x.test");
        await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, ownsHttpClient: true));
        var result = await mcp.CallToolAsync("create_item", new Dictionary<string, object?>
        {
            ["request"] = new Dictionary<string, object?> { ["title"] = "B probe", ["sourceKey"] = Key },
        });

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("source key belongs to an item you can't access", text);
        Assert.DoesNotContain("A secret", text);
    }

    [Fact]
    public async Task Creator_replays_an_unfiled_item_and_others_are_forbidden()
    {
        var a = Factory.ApiClient("a@x.test");
        var item = (await (await a.PostAsJsonAsync("/items", Keyed(null, "A unfiled", Key))).Content.ReadFromJsonAsync<CalendarItemDto>())!;

        var replay = await a.PostAsJsonAsync("/items", Keyed(null, "A unfiled", Key));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(item.Id, (await replay.Content.ReadFromJsonAsync<CalendarItemDto>())!.Id);
        var batch = (await (await a.PostAsJsonAsync("/items/batch", new CreateCalendarItemsBatchRequest { Items = [Keyed(null, "A unfiled", Key)] }))
            .Content.ReadFromJsonAsync<List<ItemBatchResult>>())!;
        Assert.Equal("existed", batch[0].Status);
        Assert.Equal(item.Id, batch[0].ItemId);

        var probe = await Factory.ApiClient("b@x.test").PostAsJsonAsync("/items", Keyed(null, "B probe", Key));
        Assert.Equal(HttpStatusCode.Forbidden, probe.StatusCode);
        Assert.DoesNotContain("A unfiled", await probe.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Creator_restores_their_deleted_unfiled_item()
    {
        var a = Factory.ApiClient("a@x.test");
        var item = (await (await a.PostAsJsonAsync("/items", Keyed(null, "A unfiled", Key))).Content.ReadFromJsonAsync<CalendarItemDto>())!;
        await using (var s = Store.LightweightSession())
        {
            s.Events.Append(item.Id, new ItemDeleted(item.Id, DateTimeOffset.UtcNow));
            await s.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await Factory.ApiClient("b@x.test").PostAsJsonAsync("/items", Keyed(null, "B takeover", Key))).StatusCode);

        var recreate = await a.PostAsJsonAsync("/items", Keyed(null, "A again", Key));
        Assert.Equal(HttpStatusCode.OK, recreate.StatusCode);
        Assert.Equal(item.Id, (await recreate.Content.ReadFromJsonAsync<CalendarItemDto>())!.Id);
        await using var q = Store.QuerySession();
        var restored = await q.LoadAsync<CalendarItem>(item.Id);
        Assert.Null(restored!.DeletedAt);
        Assert.Equal("A again", restored.Title);
    }
}
