using LupiraCalApi.Dtos.Calendars;
using LupiraCalApi.Dtos.Contacts;
using LupiraCalApi.Dtos.Me;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Base for integration tests: shares the container fixture, resets Marten data before each test, and
/// provides DAV/ICS/XML helpers. Lives in the "integration" collection so tests run serially against the shared DB.</summary>
[Collection("integration")]
public abstract class IntegrationTest(CalApiTestFactory factory) : IAsyncLifetime
{
    protected readonly CalApiTestFactory Factory = factory;

    protected static readonly XNamespace D = "DAV:";
    protected static readonly XNamespace C = "urn:ietf:params:xml:ns:caldav";
    protected static readonly XNamespace CR = "urn:ietf:params:xml:ns:carddav";
    protected static readonly XNamespace CS = "http://calendarserver.org/ns/";

    public async Task InitializeAsync() => await Factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---- REST fixture helpers ----

    protected static async Task<Guid> GetMyIdAsync(HttpClient api)
    {
        var me = await api.GetFromJsonAsync<MeDto>("/me");
        return me!.Id;
    }

    protected static async Task<Guid> CreateCalendarAsync(HttpClient api, string slug = "work", string? displayName = "Work")
    {
        var resp = await api.PostAsJsonAsync("/calendars", new CreateCalendarRequest { Slug = slug, DisplayName = displayName, Kind = "calendar", DefaultTimezone = "UTC" });
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<ContainerDto>();
        return dto!.Id;
    }

    protected static async Task<Guid> CreateAddressBookAsync(HttpClient api, string slug = "people", string? displayName = "People")
    {
        var resp = await api.PostAsJsonAsync("/calendars", new CreateCalendarRequest { Slug = slug, DisplayName = displayName, Kind = "addressbook" });
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<ContainerDto>();
        return dto!.Id;
    }

    protected static async Task<ContactDto> CreateContactAsync(HttpClient api, Guid addressBookId, string given = "Jane", string family = "Doe", string? email = null)
    {
        var req = new CreateContactRequest { AddressBookId = addressBookId, GivenName = given, FamilyName = family, Emails = email is null ? null : [email] };
        var resp = await api.PostAsJsonAsync("/contacts", req);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContactDto>())!;
    }

    // ---- DAV request helper (custom verbs + preconditions) ----

    protected static async Task<HttpResponseMessage> SendDav(
        HttpClient client, string method, string url, string? body = null, string? depth = null,
        string? ifMatch = null, string? ifNoneMatch = null, string contentType = "application/xml")
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        if (depth is not null) req.Headers.TryAddWithoutValidation("Depth", depth);
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (ifNoneMatch is not null) req.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        if (body is not null) req.Content = new StringContent(body, Encoding.UTF8, contentType);
        return await client.SendAsync(req);
    }

    protected static async Task<XDocument> ReadXml(HttpResponseMessage resp) =>
        XDocument.Parse(await resp.Content.ReadAsStringAsync());

    // ---- payload builders ----

    protected static string MinimalIcs(string uid, string summary, DateTimeOffset startUtc, string? rrule = null)
    {
        var dt = startUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
        var end = startUtc.AddHours(1).UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//lupira-test//EN\r\nBEGIN:VEVENT\r\n");
        sb.Append($"UID:{uid}\r\nSUMMARY:{summary}\r\nDTSTART:{dt}\r\nDTEND:{end}\r\n");
        if (rrule is not null) sb.Append($"RRULE:{rrule}\r\n");
        sb.Append("END:VEVENT\r\nEND:VCALENDAR\r\n");
        return sb.ToString();
    }

    protected static string MinimalIcsAllDay(string uid, string summary, DateOnly start)
    {
        var d = start.ToString("yyyyMMdd");
        var end = start.AddDays(1).ToString("yyyyMMdd");
        return $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//lupira-test//EN\r\nBEGIN:VEVENT\r\nUID:{uid}\r\nSUMMARY:{summary}\r\nDTSTART;VALUE=DATE:{d}\r\nDTEND;VALUE=DATE:{end}\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
    }

    protected static string MinimalVcf(string uid, string fullName, string? email = null)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCARD\r\nVERSION:3.0\r\n");
        sb.Append($"UID:{uid}\r\nFN:{fullName}\r\nN:{fullName};;;;\r\n");
        if (email is not null) sb.Append($"EMAIL:{email}\r\n");
        sb.Append("END:VCARD\r\n");
        return sb.ToString();
    }

    protected static string SyncCollectionBody(string? token) =>
        $"""<?xml version="1.0" encoding="utf-8"?><d:sync-collection xmlns:d="DAV:"><d:sync-token>{token}</d:sync-token><d:sync-level>1</d:sync-level><d:prop><d:getetag/></d:prop></d:sync-collection>""";

    protected static string TimeRangeQueryBody(DateTimeOffset start, DateTimeOffset end)
    {
        var s = start.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
        var e = end.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
        return $"""<?xml version="1.0" encoding="utf-8"?><c:calendar-query xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav"><d:prop><d:getetag/><c:calendar-data/></d:prop><c:filter><c:comp-filter name="VCALENDAR"><c:comp-filter name="VEVENT"><c:time-range start="{s}" end="{e}"/></c:comp-filter></c:comp-filter></c:filter></c:calendar-query>""";
    }

    protected static string MultigetBody(params string[] hrefs)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="utf-8"?><c:calendar-multiget xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav"><d:prop><d:getetag/><c:calendar-data/></d:prop>""");
        foreach (var h in hrefs) sb.Append($"<d:href>{h}</d:href>");
        sb.Append("</c:calendar-multiget>");
        return sb.ToString();
    }

    protected static string AddressbookMultigetBody(params string[] hrefs)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="utf-8"?><c:addressbook-multiget xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:carddav"><d:prop><d:getetag/><c:address-data/></d:prop>""");
        foreach (var h in hrefs) sb.Append($"<d:href>{h}</d:href>");
        sb.Append("</c:addressbook-multiget>");
        return sb.ToString();
    }

    /// <summary>The sync-token value carried at the multistatus root (RFC 6578), or null if absent.</summary>
    protected static string? RootSyncToken(XDocument doc) =>
        doc.Root?.Elements(D + "sync-token").FirstOrDefault()?.Value;
}
