using System.Net;
using System.Net.Http.Json;
using Lupira.Primitives;
using LupiraCalApi.Core.Application.Dav;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Dav;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>
/// The /dav-backend contract as the LupiraDavApi gateway consumes it: collection listing with JIT
/// provision + standard-set bootstrap (Agenda-only), query/multiget/time-range, blob round-trip with
/// deterministic ETags, PUT/DELETE with preconditions, and the sync-token changes feed with tombstones
/// (including removal-from-calendar).
/// </summary>
public sealed class DavBackendTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";
    private static readonly DateTimeOffset Start = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Collections_provision_the_principal_and_expose_only_agenda_calendars()
    {
        var api = Factory.ApiClient(Email);
        var resp = await api.GetAsync($"{DavBackendBase("fresh@x.test")}/collections");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<DavCollectionsDto>();
        // Bootstrap seeds 8 calendars; only the 4 Agenda-class ones are DAV-projected.
        Assert.Equal(4, dto!.Collections.Count);
        Assert.All(dto.Collections, c => Assert.Equal(DavCollectionKind.EventCalendar, c.Kind));
        Assert.Contains(dto.Collections, c => c.DisplayName == "Personal");
        Assert.All(dto.Collections, c => Assert.StartsWith("seq-", c.Ctag));
    }

    [Fact]
    public async Task Put_get_roundtrip_has_a_deterministic_etag()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var ics = MinimalIcs("evt-1@x", "Standup", Start);

        var put = await PutIcsBackendAsync(api, Email, cal, "evt-1@x", ics);
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);
        var etag = put.Headers.ETag!.Tag.Trim('"');

        var get = await GetIcsBackendAsync(api, Email, cal, "evt-1@x");
        get.EnsureSuccessStatusCode();
        Assert.Equal(etag, get.Headers.ETag!.Tag.Trim('"'));
        var body = await get.Content.ReadAsStringAsync();
        Assert.Contains("SUMMARY:Standup", body);
        Assert.Contains("UID:evt-1@x", body);

        // Same content re-PUT unconditionally → same canonical ETag (retry-safe by construction).
        var again = await PutIcsBackendAsync(api, Email, cal, "evt-1@x", ics);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(etag, again.Headers.ETag!.Tag.Trim('"'));
    }

    [Fact]
    public async Task Query_supports_listing_multiget_and_time_range()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        await PutIcsBackendAsync(api, Email, cal, "july@x", MinimalIcs("july@x", "In window", Start));
        await PutIcsBackendAsync(api, Email, cal, "sept@x", MinimalIcs("sept@x", "Out of window", Start.AddMonths(2)));
        await PutIcsBackendAsync(api, Email, cal, "weekly@x", MinimalIcs("weekly@x", "Recurring", Start.AddMonths(-2), rrule: "FREQ=WEEKLY"));

        var all = await QueryAsync(api, cal, new DavQueryRequest());
        Assert.Equal(3, all.Resources.Count);
        Assert.All(all.Resources, r => Assert.Null(r.Content));

        var multiget = await QueryAsync(api, cal, new DavQueryRequest { Uids = ["july@x"], IncludeContent = true });
        var one = Assert.Single(multiget.Resources);
        Assert.Contains("SUMMARY:In window", one.Content);

        // July window: the July event + the weekly recurrence expanded into it; September stays out.
        var window = await QueryAsync(api, cal, new DavQueryRequest { Start = Start.AddDays(-1), End = Start.AddDays(30) });
        var uids = window.Resources.Select(r => r.Uid).ToList();
        Assert.Contains("july@x", uids);
        Assert.Contains("weekly@x", uids);
        Assert.DoesNotContain("sept@x", uids);
    }

    [Fact]
    public async Task All_day_put_stores_the_inclusive_last_day_and_serves_the_exclusive_end()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var day = new DateOnly(2026, 7, 10);

        (await PutIcsBackendAsync(api, Email, cal, "day@x", MinimalIcsAllDay("day@x", "Holiday", day))).EnsureSuccessStatusCode();

        var item = await api.GetFromJsonAsync<CalendarItemDto>($"/items/{DeterministicGuid.From("day@x")}");
        Assert.Equal(day, item!.StartDate);
        Assert.Equal(day, item.EndDate);
        Assert.Contains("DTEND;VALUE=DATE:20260711", await (await GetIcsBackendAsync(api, Email, cal, "day@x")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Put_on_a_nested_item_keeps_its_parent()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var trip = await PostItemAsync(api, new CreateCalendarItemRequest { CalendarId = cal, Title = "Trip", IsAllDay = true, StartDate = new DateOnly(2026, 7, 1) });
        var leg = await PostItemAsync(api, new CreateCalendarItemRequest
        {
            CalendarId = cal, Title = "Flight", StartsAt = Start, EndsAt = Start.AddHours(2), SourceKey = "leg@x", ParentItemId = trip.Id,
        });

        (await PutIcsBackendAsync(api, Email, cal, "leg@x", MinimalIcs("leg@x", "Flight SK123", Start))).EnsureSuccessStatusCode();

        var after = await api.GetFromJsonAsync<CalendarItemDto>($"/items/{leg.Id}");
        Assert.Equal("Flight SK123", after!.Title);
        Assert.Equal(trip.Id, after.ParentItemId);
    }

    private static async Task<CalendarItemDto> PostItemAsync(HttpClient api, CreateCalendarItemRequest request)
    {
        var resp = await api.PostAsJsonAsync("/items", request);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    [Fact]
    public async Task Put_preconditions_guard_create_and_update()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var ics = MinimalIcs("evt-2@x", "Original", Start);

        var create = await PutIcsBackendAsync(api, Email, cal, "evt-2@x", ics, ifNoneMatchStar: true);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var etag = create.Headers.ETag!.Tag.Trim('"');

        Assert.Equal(HttpStatusCode.PreconditionFailed,
            (await PutIcsBackendAsync(api, Email, cal, "evt-2@x", ics, ifNoneMatchStar: true)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed,
            (await PutIcsBackendAsync(api, Email, cal, "evt-2@x", MinimalIcs("evt-2@x", "Renamed", Start), ifMatch: "stale")).StatusCode);

        var update = await PutIcsBackendAsync(api, Email, cal, "evt-2@x", MinimalIcs("evt-2@x", "Renamed", Start), ifMatch: etag);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.NotEqual(etag, update.Headers.ETag!.Tag.Trim('"'));
    }

    [Fact]
    public async Task Changes_diff_and_tombstone_deletes_and_membership_removals()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        await PutIcsBackendAsync(api, Email, cal, "keep@x", MinimalIcs("keep@x", "Keep", Start));
        await PutIcsBackendAsync(api, Email, cal, "gone@x", MinimalIcs("gone@x", "Gone", Start));

        var full = await ChangesAsync(api, cal, null);
        Assert.Equal(2, full.Changed.Count);
        Assert.Empty(full.Deleted);

        // DAV DELETE removes the resource from THIS calendar → tombstone on the next diff.
        Assert.Equal(HttpStatusCode.NoContent,
            (await api.DeleteAsync($"{DavBackendBase(Email)}/collections/{cal}/resources/gone@x")).StatusCode);
        var diff = await ChangesAsync(api, cal, full.SyncToken);
        Assert.Contains("gone@x", diff.Deleted);
        Assert.DoesNotContain(diff.Changed, c => c.Uid == "gone@x");

        // Unknown token degrades to the full live listing (self-healing resync).
        var healed = await ChangesAsync(api, cal, "garbage");
        Assert.Single(healed.Changed, c => c.Uid == "keep@x");
        Assert.Empty(healed.Deleted);
    }

    [Fact]
    public async Task Advancing_the_resync_epoch_turns_outstanding_tokens_into_a_full_listing()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        await PutIcsBackendAsync(api, Email, cal, "keep@x", MinimalIcs("keep@x", "Keep", Start));
        var before = await ChangesAsync(api, cal, null);
        Assert.Empty((await ChangesAsync(api, cal, before.SyncToken)).Changed);   // incremental: nothing new

        await DavChangeFeed.AdvanceEpochAsync(Store);

        var after = await ChangesAsync(api, cal, before.SyncToken);
        Assert.Single(after.Changed, c => c.Uid == "keep@x");                      // old token → full listing
        Assert.StartsWith("1.", after.SyncToken);
        Assert.Empty((await ChangesAsync(api, cal, after.SyncToken)).Changed);    // new token is incremental again
        var collections = await (await api.GetAsync($"{DavBackendBase(Email)}/collections")).Content.ReadFromJsonAsync<DavCollectionsDto>();
        Assert.All(collections!.Collections, c => Assert.StartsWith("seq-1.", c.Ctag));
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var anon = Factory.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"{DavBackendBase(Email)}/collections")).StatusCode);
    }

    private static async Task<DavResourcesDto> QueryAsync(HttpClient api, Guid cal, DavQueryRequest body)
    {
        var resp = await api.PostAsJsonAsync($"{DavBackendBase(Email)}/collections/{cal}/query", body);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<DavResourcesDto>())!;
    }

    private static async Task<DavChangesDto> ChangesAsync(HttpClient api, Guid cal, string? since)
    {
        var url = $"{DavBackendBase(Email)}/collections/{cal}/changes" + (since is null ? "" : $"?since={Uri.EscapeDataString(since)}");
        var resp = await api.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<DavChangesDto>())!;
    }
}
