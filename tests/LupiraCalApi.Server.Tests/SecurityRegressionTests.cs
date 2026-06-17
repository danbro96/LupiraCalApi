using LupiraCalApi.Dtos.CalendarItems;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.Server.Tests;

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
        new(calId, title, null, null, null, false, Start, Start.AddHours(1), "UTC", null, null, null, null, null);

    // ---------- Vuln 1: curation IDOR (CurationService) ----------

    [Fact]
    public async Task AddToCalendar_cannot_file_another_users_item()
    {
        var a = Factory.ApiClient("a@x.test");
        var calA = await CreateCalendarAsync(a, "a-cal");
        var item = (await (await a.PostAsJsonAsync("/api/items", Event(calA, "A secret"))).Content.ReadFromJsonAsync<CalendarItemDto>())!;

        var b = Factory.ApiClient("b@x.test");
        var calB = await CreateCalendarAsync(b, "b-cal");

        // B tries to file A's item into B's own calendar to self-grant read/write access.
        var add = await b.PostAsync($"/api/items/{item.Id}/calendars/{calB}?status=accepted", null);
        Assert.Equal(HttpStatusCode.NotFound, add.StatusCode);

        // B still cannot read A's item.
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync($"/api/items/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task Accept_cannot_accept_item_not_proposed_into_my_calendar()
    {
        var a = Factory.ApiClient("a@x.test");
        var calA = await CreateCalendarAsync(a, "a-cal");
        var item = (await (await a.PostAsJsonAsync("/api/items", Event(calA, "A secret"))).Content.ReadFromJsonAsync<CalendarItemDto>())!;

        var b = Factory.ApiClient("b@x.test");
        var calB = await CreateCalendarAsync(b, "b-cal");

        var accept = await b.PostAsync($"/api/items/{item.Id}/calendars/{calB}/accept", null);
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
    }

    [Fact] // no-regression: filing your own unfiled item must still work
    public async Task AddToCalendar_files_my_own_unfiled_item()
    {
        var a = Factory.ApiClient("a@x.test");
        var calA = await CreateCalendarAsync(a, "a-cal");
        var item = (await (await a.PostAsJsonAsync("/api/items", Event(null, "Unfiled"))).Content.ReadFromJsonAsync<CalendarItemDto>())!;

        var add = await a.PostAsync($"/api/items/{item.Id}/calendars/{calA}?status=accepted", null);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync($"/api/items/{item.Id}")).StatusCode);
    }

    // ---------- Vuln 2: DAV cross-tenant by UID ----------

    [Fact]
    public async Task Dav_put_cannot_overwrite_another_users_item_by_uid()
    {
        const string uid = "evt-shared@x";
        var a = Factory.ApiClient("a@x.test");
        var aId = await GetMyIdAsync(a);
        var calA = await CreateCalendarAsync(a, "a-cal");
        var aDav = Factory.DavClient("a@x.test");
        var aUrl = $"/dav/u/{aId}/cal/{calA}/{uid}.ics";
        var aIcs = MinimalIcs(uid, "A meeting", Start);
        Assert.Equal(HttpStatusCode.Created, (await SendDav(aDav, "PUT", aUrl, body: aIcs, contentType: "text/calendar")).StatusCode);

        var b = Factory.ApiClient("b@x.test");
        var bId = await GetMyIdAsync(b);
        var calB = await CreateCalendarAsync(b, "b-cal");
        var bDav = Factory.DavClient("b@x.test");
        var bUrl = $"/dav/u/{bId}/cal/{calB}/{uid}.ics";
        var bIcs = MinimalIcs(uid, "B hijack", Start.AddDays(1));

        // B PUTs the same UID into B's own calendar — must not touch A's item.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendDav(bDav, "PUT", bUrl, body: bIcs, contentType: "text/calendar")).StatusCode);

        // A's item is unchanged (byte-identical to the original PUT).
        var get = await aDav.GetAsync(aUrl);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(aIcs, await get.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dav_put_vcard_cannot_hijack_another_users_contact_by_uid()
    {
        const string uid = "card-shared@x";
        var a = Factory.ApiClient("a@x.test");
        var aId = await GetMyIdAsync(a);
        var abA = await CreateAddressBookAsync(a, "a-book");
        var aDav = Factory.DavClient("a@x.test");
        var aUrl = $"/dav/u/{aId}/card/{abA}/{uid}.vcf";
        var aVcf = MinimalVcf(uid, "Alice Contact", "alice.contact@x.test");
        Assert.Equal(HttpStatusCode.Created, (await SendDav(aDav, "PUT", aUrl, body: aVcf, contentType: "text/vcard")).StatusCode);

        var b = Factory.ApiClient("b@x.test");
        var bId = await GetMyIdAsync(b);
        var abB = await CreateAddressBookAsync(b, "b-book");
        var bDav = Factory.DavClient("b@x.test");
        var bUrl = $"/dav/u/{bId}/card/{abB}/{uid}.vcf";
        var bVcf = MinimalVcf(uid, "B Hijack", "b@x.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await SendDav(bDav, "PUT", bUrl, body: bVcf, contentType: "text/vcard")).StatusCode);

        // A's contact is untouched and still in A's book.
        var body = await (await aDav.GetAsync(aUrl)).Content.ReadAsStringAsync();
        Assert.Contains("Alice Contact", body);
        Assert.DoesNotContain("B Hijack", body);
    }

    [Fact]
    public async Task Dav_delete_vcard_cannot_delete_another_users_contact_by_uid()
    {
        const string uid = "card-del@x";
        var a = Factory.ApiClient("a@x.test");
        var aId = await GetMyIdAsync(a);
        var abA = await CreateAddressBookAsync(a, "a-book");
        var aDav = Factory.DavClient("a@x.test");
        var aUrl = $"/dav/u/{aId}/card/{abA}/{uid}.vcf";
        Assert.Equal(HttpStatusCode.Created, (await SendDav(aDav, "PUT", aUrl, body: MinimalVcf(uid, "Alice Contact"), contentType: "text/vcard")).StatusCode);

        var b = Factory.ApiClient("b@x.test");
        var bId = await GetMyIdAsync(b);
        var abB = await CreateAddressBookAsync(b, "b-book");
        var bDav = Factory.DavClient("b@x.test");
        var bUrl = $"/dav/u/{bId}/card/{abB}/{uid}.vcf";

        Assert.Equal(HttpStatusCode.NotFound, (await SendDav(bDav, "DELETE", bUrl)).StatusCode);

        // A's contact is still present.
        Assert.Equal(HttpStatusCode.OK, (await aDav.GetAsync(aUrl)).StatusCode);
    }
}
