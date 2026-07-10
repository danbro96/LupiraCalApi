using LupiraCalApi.Dtos.CalendarItems;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>
/// Regression tests for the two broken-object-level-authorization bugs found in the security review:
/// (1) curation IDOR — filing a foreign item into your own calendar to self-grant access; and
/// (2) DAV cross-tenant overwrite/delete by UID — item/contact streams are keyed by UID alone, so knowing a
/// victim's iCal/vCard UID let an attacker overwrite, re-file, or delete the victim's resource.
/// </summary>
public sealed class SecurityRegressionTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    static readonly DateTimeOffset Start = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    static CreateCalendarItemRequest Event(Guid? calId, string title) =>
        new() { CalendarId = calId, Title = title, IsAllDay = false, StartsAt = Start, EndsAt = Start.AddHours(1), StartTimezone = "UTC" };

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
}
