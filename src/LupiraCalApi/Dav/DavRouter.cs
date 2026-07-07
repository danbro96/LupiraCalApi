using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Serialization;
using Marten;
using System.Globalization;
using System.Xml.Linq;
using DavCalendar = LupiraCalApi.Domain.Calendar;   // disambiguate from System.Globalization.Calendar

namespace LupiraCalApi.Dav;

/// <summary>
/// CalDAV (RFC 4791) + CardDAV (RFC 6352) over the Marten store. Reads come from the inline <see cref="CalendarItem"/>
/// / <see cref="Contact"/> snapshots; writes append events via the Core services. The sync-token + ctag are derived
/// from Marten's global event <c>Sequence</c> (opaque, monotonic) — no Revision column. Only items <em>accepted</em>
/// into a calendar are exposed. URL layout (all discovered, never typed):
///   /dav/ → root; /dav/u/{userId}/ → principal; /dav/u/{userId}/cal/{calId}/{uid}.ics → an item; .../card/{abId}/{uid}.vcf → a contact.
/// Two-account model: a principal addresses only its own /u/{id}/ tree (sharing is enforced by AccessResolver).
/// A principal with no calendar grants is bootstrapped with the standard container set on first request, so
/// DAV-only members self-provision without ever touching REST.
/// </summary>
public static class DavRouter
{
    static readonly XNamespace D = "DAV:";
    static readonly XNamespace C = "urn:ietf:params:xml:ns:caldav";
    static readonly XNamespace CR = "urn:ietf:params:xml:ns:carddav";
    static readonly XNamespace CS = "http://calendarserver.org/ns/";

    public static async Task Handle(HttpContext ctx)
    {
        var method = ctx.Request.Method.ToUpperInvariant();
        var ct = ctx.RequestAborted;

        if (method == "OPTIONS") { WriteOptions(ctx); return; }

        if (method is "MKCALENDAR" or "MKCOL" or "PROPPATCH" or "MOVE" or "COPY" or "LOCK" or "UNLOCK")
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var session = ctx.RequestServices.GetRequiredService<IQuerySession>();
        var access = ctx.RequestServices.GetRequiredService<AccessResolver>();
        var user = await ctx.RequestServices.GetRequiredService<CurrentUser>().GetAsync(ct);

        // A DAV-only principal (JIT-provisioned by Basic auth) has no containers yet — seed the standard set on first contact.
        if ((await access.AccessibleCalendarIdsAsync(user.Id, ct)).Count == 0)
            await ctx.RequestServices.GetRequiredService<CalendarService>().BootstrapPersonalAsync(user.Id, ct);

        var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        var segments = (ctx.Request.Path.Value ?? "").Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var rest = segments.Skip(1).ToArray();   // segments[0] == "dav"

        if (rest.Length >= 2 && rest[0] == "u" && rest[1] != user.Id.ToString())
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var depth = ctx.Request.Headers.TryGetValue("Depth", out var dh) ? dh.ToString() : "0";
        var deep = depth is "1" or "infinity";

        if (rest.Length == 0)
        {
            if (method == "PROPFIND") { await WriteMultiStatus(ctx, RootPropfind(baseUrl, user)); return; }
        }
        else if (rest[0] == "u" && rest.Length >= 2)
        {
            if (rest.Length == 2 && method == "PROPFIND") { await WriteMultiStatus(ctx, PrincipalPropfind(baseUrl, user)); return; }

            if (rest.Length >= 3 && rest[2] == "cal")
            {
                if (rest.Length == 3 && method == "PROPFIND") { await WriteMultiStatus(ctx, await CalendarHomePropfind(session, access, baseUrl, user, deep, ct)); return; }
                if (rest.Length == 4)
                {
                    var calId = Guid.Parse(rest[3]);
                    if (method == "PROPFIND") { await WriteMultiStatus(ctx, await CalendarPropfind(session, access, baseUrl, user, calId, deep, ct)); return; }
                    if (method == "REPORT") { await HandleCalendarReport(ctx, session, access, baseUrl, user, calId); return; }
                }
                if (rest.Length == 5)
                {
                    var calId = Guid.Parse(rest[3]); var uid = DavProtocol.StripExt(rest[4]);
                    if (method is "GET" or "HEAD") { await GetItem(ctx, session, access, user, calId, uid); return; }
                    if (method == "PUT") { await HandlePutItem(ctx, user, calId, uid); return; }
                    if (method == "DELETE") { await HandleDeleteItem(ctx, user, calId, uid); return; }
                }
            }
            else if (rest.Length >= 3 && rest[2] == "card")
            {
                if (rest.Length == 3 && method == "PROPFIND") { await WriteMultiStatus(ctx, await AddressbookHomePropfind(session, access, baseUrl, user, deep, ct)); return; }
                if (rest.Length == 4)
                {
                    var abId = Guid.Parse(rest[3]);
                    if (method == "PROPFIND") { await WriteMultiStatus(ctx, await AddressbookPropfind(session, access, baseUrl, user, abId, deep, ct)); return; }
                    if (method == "REPORT") { await HandleAddressbookReport(ctx, session, access, baseUrl, user, abId); return; }
                }
                if (rest.Length == 5)
                {
                    var abId = Guid.Parse(rest[3]); var uid = DavProtocol.StripExt(rest[4]);
                    if (method is "GET" or "HEAD") { await GetContact(ctx, session, access, user, abId, uid); return; }
                    if (method == "PUT") { await HandlePutContact(ctx, user, abId, uid); return; }
                    if (method == "DELETE") { await HandleDeleteContact(ctx, user, abId, uid); return; }
                }
            }
        }

        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    // ---------- token (opaque, monotonic) = Marten's current global event sequence ----------

    static async Task<long> CurrentTokenAsync(IQuerySession session, CancellationToken ct)
    {
        var last = await session.Events.QueryAllRawEvents().OrderByDescending(e => e.Sequence).Take(1).ToListAsync(ct);
        return last.Count > 0 ? last[0].Sequence : 0L;
    }

    // ---------- PROPFIND builders ----------

    static XElement RootPropfind(string baseUrl, Principal user) => MultiStatus(
        Response($"{baseUrl}/dav/",
            new XElement(D + "resourcetype", new XElement(D + "collection")),
            new XElement(D + "current-user-principal", Href($"{baseUrl}/dav/u/{user.Id}/"))));

    static XElement PrincipalPropfind(string baseUrl, Principal user) => MultiStatus(
        Response($"{baseUrl}/dav/u/{user.Id}/",
            new XElement(D + "resourcetype", new XElement(D + "collection"), new XElement(D + "principal")),
            new XElement(D + "displayname", user.DisplayName ?? user.Email),
            new XElement(D + "current-user-principal", Href($"{baseUrl}/dav/u/{user.Id}/")),
            new XElement(D + "principal-URL", Href($"{baseUrl}/dav/u/{user.Id}/")),
            new XElement(C + "calendar-home-set", Href($"{baseUrl}/dav/u/{user.Id}/cal/")),
            new XElement(CR + "addressbook-home-set", Href($"{baseUrl}/dav/u/{user.Id}/card/"))));

    static async Task<XElement> CalendarHomePropfind(IQuerySession session, AccessResolver access, string baseUrl, Principal user, bool deep, CancellationToken ct)
    {
        var responses = new List<XElement>
        {
            Response($"{baseUrl}/dav/u/{user.Id}/cal/", new XElement(D + "resourcetype", new XElement(D + "collection"))),
        };
        if (deep)
        {
            var ids = await access.AccessibleCalendarIdsAsync(user.Id, ct);
            // Only agenda calendars are DAV-projected; system calendars (Inbox/LlmPrompts/UserCheckIn/DevOps) are REST/DB-only.
            var cals = await session.Query<DavCalendar>().Where(c => ids.Contains(c.Id) && c.Class == CalendarClass.Agenda).ToListAsync(ct);
            var token = await CurrentTokenAsync(session, ct);
            foreach (var c in cals)
                responses.Add(Response($"{baseUrl}/dav/u/{user.Id}/cal/{c.Id}/", CalendarProps(c, token)));
        }
        return MultiStatus([.. responses]);
    }

    static async Task<XElement> CalendarPropfind(IQuerySession session, AccessResolver access, string baseUrl, Principal user, Guid calId, bool deep, CancellationToken ct)
    {
        if (!await access.CanReadCalendarAsync(user.Id, calId, ct)) return MultiStatus();
        var cal = await session.LoadAsync<DavCalendar>(calId, ct);
        if (cal is null || cal.Class != CalendarClass.Agenda) return MultiStatus();

        var token = await CurrentTokenAsync(session, ct);
        var responses = new List<XElement> { Response($"{baseUrl}/dav/u/{user.Id}/cal/{cal.Id}/", CalendarProps(cal, token)) };
        if (deep)
        {
            foreach (var i in await AcceptedItemsAsync(session, calId, ct))
                responses.Add(Response($"{baseUrl}/dav/u/{user.Id}/cal/{cal.Id}/{i.IcalUid}.ics",
                    new XElement(D + "getetag", Etag(i.ContentHash)),
                    new XElement(D + "getcontenttype", "text/calendar; charset=utf-8")));
        }
        return MultiStatus([.. responses]);
    }

    static XElement[] CalendarProps(DavCalendar c, long token) =>
    [
        new XElement(D + "resourcetype", new XElement(D + "collection"), new XElement(C + "calendar")),
        new XElement(D + "displayname", c.DisplayName ?? c.Slug),
        new XElement(CS + "getctag", $"\"seq-{token}\""),
        new XElement(D + "sync-token", $"{token}"),
        new XElement(C + "supported-calendar-component-set", new XElement(C + "comp", new XAttribute("name", "VEVENT"))),
        SupportedReports(C + "calendar-query", C + "calendar-multiget", D + "sync-collection"),
    ];

    static async Task<XElement> AddressbookHomePropfind(IQuerySession session, AccessResolver access, string baseUrl, Principal user, bool deep, CancellationToken ct)
    {
        var responses = new List<XElement>
        {
            Response($"{baseUrl}/dav/u/{user.Id}/card/", new XElement(D + "resourcetype", new XElement(D + "collection"))),
        };
        if (deep)
        {
            var ids = await access.AccessibleAddressBookIdsAsync(user.Id, ct);
            var books = await session.Query<AddressBook>().Where(a => ids.Contains(a.Id)).ToListAsync(ct);
            var token = await CurrentTokenAsync(session, ct);
            foreach (var a in books)
                responses.Add(Response($"{baseUrl}/dav/u/{user.Id}/card/{a.Id}/", AddressbookProps(a, token)));
        }
        return MultiStatus([.. responses]);
    }

    static async Task<XElement> AddressbookPropfind(IQuerySession session, AccessResolver access, string baseUrl, Principal user, Guid abId, bool deep, CancellationToken ct)
    {
        if (!await access.CanReadAddressBookAsync(user.Id, abId, ct)) return MultiStatus();
        var book = await session.LoadAsync<AddressBook>(abId, ct);
        if (book is null) return MultiStatus();

        var token = await CurrentTokenAsync(session, ct);
        var responses = new List<XElement> { Response($"{baseUrl}/dav/u/{user.Id}/card/{book.Id}/", AddressbookProps(book, token)) };
        if (deep)
        {
            var contacts = await session.Query<Contact>().Where(x => x.AddressBookId == abId && x.DeletedAt == null).ToListAsync(ct);
            foreach (var x in contacts)
                responses.Add(Response($"{baseUrl}/dav/u/{user.Id}/card/{book.Id}/{x.VcardUid}.vcf",
                    new XElement(D + "getetag", Etag(x.ContentHash)),
                    new XElement(D + "getcontenttype", "text/vcard; charset=utf-8")));
        }
        return MultiStatus([.. responses]);
    }

    static XElement[] AddressbookProps(AddressBook a, long token) =>
    [
        new XElement(D + "resourcetype", new XElement(D + "collection"), new XElement(CR + "addressbook")),
        new XElement(D + "displayname", a.DisplayName ?? a.Slug),
        new XElement(CS + "getctag", $"\"seq-{token}\""),
        new XElement(D + "sync-token", $"{token}"),
        SupportedReports(CR + "addressbook-query", CR + "addressbook-multiget", D + "sync-collection"),
    ];

    // ---------- REPORT ----------

    static async Task HandleCalendarReport(HttpContext ctx, IQuerySession session, AccessResolver access, string baseUrl, Principal user, Guid calId)
    {
        var ct = ctx.RequestAborted;
        if (!await access.CanReadCalendarAsync(user.Id, calId, ct)) { ctx.Response.StatusCode = 404; return; }
        var body = await ReadBody(ctx);
        var doc = DavProtocol.TryParseXml(body);

        if (doc?.Root?.Name == D + "sync-collection") { await CalendarSync(ctx, session, baseUrl, user, calId, doc); return; }

        var items = await AcceptedItemsAsync(session, calId, ct);
        var requested = DavProtocol.ExtractHrefUids(body, ".ics");
        if (requested.Count > 0)
        {
            items = [.. items.Where(i => requested.Contains(i.IcalUid))];                  // calendar-multiget
        }
        else if (DavProtocol.ParseTimeRange(doc) is { } range)                              // calendar-query time-range
        {
            var exp = ctx.RequestServices.GetRequiredService<RecurrenceExpander>();
            items = [.. items.Where(i => DavProtocol.OverlapsWindow(i, range.Start, range.End, exp))];
        }

        var responses = new List<XElement>();
        foreach (var i in items)
            responses.Add(Response($"{baseUrl}/dav/u/{user.Id}/cal/{calId}/{i.IcalUid}.ics",
                new XElement(D + "getetag", Etag(i.ContentHash)),
                new XElement(C + "calendar-data", await ItemIcsAsync(ctx, i, ct))));
        await WriteMultiStatus(ctx, MultiStatus([.. responses]));
    }

    /// <summary>Regenerate an item's canonical ICS (location resolved from its Place), so DAV never depends on a stored blob.</summary>
    static async Task<string> ItemIcsAsync(HttpContext ctx, CalendarItem i, CancellationToken ct)
    {
        var label = await ctx.RequestServices.GetRequiredService<PlaceService>().LabelOfAsync(i.PlaceId, ct);
        return ICalSerializer.From(i, label);
    }

    static async Task CalendarSync(HttpContext ctx, IQuerySession session, string baseUrl, Principal user, Guid calId, XDocument doc)
    {
        var ct = ctx.RequestAborted;
        var token = DavProtocol.ParseSyncToken(doc);
        var newToken = await CurrentTokenAsync(session, ct);
        var responses = new List<XElement>();
        string Href(string uid) => $"{baseUrl}/dav/u/{user.Id}/cal/{calId}/{uid}.ics";

        if (token is null)   // initial sync: every accepted live resource
        {
            foreach (var i in await AcceptedItemsAsync(session, calId, ct))
                responses.Add(Response(Href(i.IcalUid), new XElement(D + "getetag", Etag(i.ContentHash))));
        }
        else                 // diffs since token: items whose stream changed, that are/were in this calendar
        {
            var changedIds = (await session.Events.QueryAllRawEvents().Where(e => e.Sequence > token).ToListAsync(ct))
                .Select(e => e.StreamId).Distinct().ToList();
            var items = await session.Query<CalendarItem>().Where(i => changedIds.Contains(i.Id)).ToListAsync(ct);
            foreach (var i in items)
            {
                var membership = i.Calendars.FirstOrDefault(m => m.CalendarId == calId);
                if (membership is null) continue;   // never been in this calendar
                responses.Add(i.DeletedAt is not null || membership.Status != CalendarEntryStatus.Accepted
                    ? DeletedResponse(Href(i.IcalUid))
                    : Response(Href(i.IcalUid), new XElement(D + "getetag", Etag(i.ContentHash))));
            }
        }
        await WriteMultiStatus(ctx, MultiStatusWithToken(newToken, [.. responses]));
    }

    static async Task HandleAddressbookReport(HttpContext ctx, IQuerySession session, AccessResolver access, string baseUrl, Principal user, Guid abId)
    {
        var ct = ctx.RequestAborted;
        if (!await access.CanReadAddressBookAsync(user.Id, abId, ct)) { ctx.Response.StatusCode = 404; return; }
        var body = await ReadBody(ctx);
        var doc = DavProtocol.TryParseXml(body);

        if (doc?.Root?.Name == D + "sync-collection") { await AddressbookSync(ctx, session, baseUrl, user, abId, doc); return; }

        var contacts = await session.Query<Contact>().Where(x => x.AddressBookId == abId && x.DeletedAt == null).ToListAsync(ct);
        var requested = DavProtocol.ExtractHrefUids(body, ".vcf");
        if (requested.Count > 0) contacts = [.. contacts.Where(x => requested.Contains(x.VcardUid))];

        var responses = contacts.Select(x => Response($"{baseUrl}/dav/u/{user.Id}/card/{abId}/{x.VcardUid}.vcf",
            new XElement(D + "getetag", Etag(x.ContentHash)),
            new XElement(CR + "address-data", VCardSerializer.From(x))));
        await WriteMultiStatus(ctx, MultiStatus([.. responses]));
    }

    static async Task AddressbookSync(HttpContext ctx, IQuerySession session, string baseUrl, Principal user, Guid abId, XDocument doc)
    {
        var ct = ctx.RequestAborted;
        var token = DavProtocol.ParseSyncToken(doc);
        var newToken = await CurrentTokenAsync(session, ct);
        var responses = new List<XElement>();
        string Href(string uid) => $"{baseUrl}/dav/u/{user.Id}/card/{abId}/{uid}.vcf";

        if (token is null)
        {
            foreach (var x in await session.Query<Contact>().Where(c => c.AddressBookId == abId && c.DeletedAt == null).ToListAsync(ct))
                responses.Add(Response(Href(x.VcardUid), new XElement(D + "getetag", Etag(x.ContentHash))));
        }
        else
        {
            var changedIds = (await session.Events.QueryAllRawEvents().Where(e => e.Sequence > token).ToListAsync(ct))
                .Select(e => e.StreamId).Distinct().ToList();
            var contacts = await session.Query<Contact>().Where(c => changedIds.Contains(c.Id) && c.AddressBookId == abId).ToListAsync(ct);
            foreach (var x in contacts)
                responses.Add(x.DeletedAt is not null
                    ? DeletedResponse(Href(x.VcardUid))
                    : Response(Href(x.VcardUid), new XElement(D + "getetag", Etag(x.ContentHash))));
        }
        await WriteMultiStatus(ctx, MultiStatusWithToken(newToken, [.. responses]));
    }

    // ---------- GET object ----------

    static async Task GetItem(HttpContext ctx, IQuerySession session, AccessResolver access, Principal user, Guid calId, string icalUid)
    {
        var ct = ctx.RequestAborted;
        if (!await access.CanReadCalendarAsync(user.Id, calId, ct)) { ctx.Response.StatusCode = 404; return; }
        var item = await session.LoadAsync<CalendarItem>(DeterministicGuid.From(icalUid), ct);
        if (item is null || item.DeletedAt is not null || !item.IsAcceptedIn(calId)) { ctx.Response.StatusCode = 404; return; }
        ctx.Response.Headers.ETag = Etag(item.ContentHash);
        ctx.Response.ContentType = "text/calendar; charset=utf-8";
        if (ctx.Request.Method == "HEAD") return;
        await ctx.Response.WriteAsync(await ItemIcsAsync(ctx, item, ct), ct);
    }

    static async Task GetContact(HttpContext ctx, IQuerySession session, AccessResolver access, Principal user, Guid abId, string vcardUid)
    {
        var ct = ctx.RequestAborted;
        if (!await access.CanReadAddressBookAsync(user.Id, abId, ct)) { ctx.Response.StatusCode = 404; return; }
        var c = await session.LoadAsync<Contact>(DeterministicGuid.From(vcardUid), ct);
        if (c is null || c.DeletedAt is not null || c.AddressBookId != abId) { ctx.Response.StatusCode = 404; return; }
        ctx.Response.Headers.ETag = Etag(c.ContentHash);
        ctx.Response.ContentType = "text/vcard; charset=utf-8";
        if (ctx.Request.Method == "HEAD") return;
        await ctx.Response.WriteAsync(VCardSerializer.From(c), ct);
    }

    // ---------- write path: object PUT / DELETE with ETag preconditions ----------

    static async Task HandlePutItem(HttpContext ctx, Principal user, Guid calId, string uid)
    {
        var raw = await ReadBody(ctx);
        var (ifMatch, ifNoneMatchStar) = Preconditions(ctx);
        var result = await ctx.RequestServices.GetRequiredService<CalendarItemService>()
            .PutIcsAsync(user.Id, calId, uid, raw, ifMatch, ifNoneMatchStar, ctx.RequestAborted);
        WriteDavWrite(ctx, result);
    }

    static async Task HandleDeleteItem(HttpContext ctx, Principal user, Guid calId, string uid)
    {
        var (ifMatch, _) = Preconditions(ctx);
        var result = await ctx.RequestServices.GetRequiredService<CalendarItemService>()
            .DeleteByUidAsync(user.Id, calId, uid, ifMatch, ctx.RequestAborted);
        WriteDavStatus(ctx, result);
    }

    static async Task HandlePutContact(HttpContext ctx, Principal user, Guid abId, string uid)
    {
        var raw = await ReadBody(ctx);
        var (ifMatch, ifNoneMatchStar) = Preconditions(ctx);
        var result = await ctx.RequestServices.GetRequiredService<ContactService>()
            .PutVcfAsync(user.Id, abId, uid, raw, ifMatch, ifNoneMatchStar, ctx.RequestAborted);
        WriteDavWrite(ctx, result);
    }

    static async Task HandleDeleteContact(HttpContext ctx, Principal user, Guid abId, string uid)
    {
        var (ifMatch, _) = Preconditions(ctx);
        var result = await ctx.RequestServices.GetRequiredService<ContactService>()
            .DeleteByUidAsync(user.Id, abId, uid, ifMatch, ctx.RequestAborted);
        WriteDavStatus(ctx, result);
    }

    static void WriteDavWrite(HttpContext ctx, OpResult<DavWriteResult> r)
    {
        if (r.Status == OpStatus.Ok && r.Value is { } w)
        {
            ctx.Response.Headers.ETag = $"\"{w.Etag}\"";
            ctx.Response.StatusCode = w.Created ? StatusCodes.Status201Created : StatusCodes.Status204NoContent;
            return;
        }
        ctx.Response.StatusCode = DavProtocol.DavStatus(r.Status);
    }

    static void WriteDavStatus(HttpContext ctx, OpResult r) => ctx.Response.StatusCode = DavProtocol.DavStatus(r.Status);

    static (string? IfMatch, bool IfNoneMatchStar) Preconditions(HttpContext ctx)
    {
        var ifMatch = ctx.Request.Headers.TryGetValue("If-Match", out var im) && im.Count > 0 ? im.ToString() : null;
        var ifNoneMatch = ctx.Request.Headers.TryGetValue("If-None-Match", out var n) ? n.ToString() : null;
        return DavProtocol.ParsePreconditions(ifMatch, ifNoneMatch);
    }

    // ---------- helpers ----------

    static async Task<List<CalendarItem>> AcceptedItemsAsync(IQuerySession session, Guid calId, CancellationToken ct)
    {
        var live = await session.Query<CalendarItem>().Where(i => i.DeletedAt == null).ToListAsync(ct);
        return [.. live.Where(i => i.IsAcceptedIn(calId))];
    }

    static void WriteOptions(HttpContext ctx)
    {
        ctx.Response.Headers["DAV"] = "1, 2, 3, calendar-access, addressbook";
        ctx.Response.Headers["Allow"] = "OPTIONS, GET, HEAD, PUT, DELETE, PROPFIND, REPORT";
        ctx.Response.StatusCode = StatusCodes.Status200OK;
    }

    static async Task WriteMultiStatus(HttpContext ctx, XElement multistatus)
    {
        ctx.Response.StatusCode = 207;
        ctx.Response.ContentType = "application/xml; charset=utf-8";
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), multistatus);
        await ctx.Response.WriteAsync(doc.Declaration + "\n" + doc.ToString(SaveOptions.DisableFormatting), ctx.RequestAborted);
    }

    static XElement MultiStatus(params XElement[] responses) => new(D + "multistatus",
        new XAttribute(XNamespace.Xmlns + "d", D.NamespaceName),
        new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName),
        new XAttribute(XNamespace.Xmlns + "cr", CR.NamespaceName),
        new XAttribute(XNamespace.Xmlns + "cs", CS.NamespaceName),
        responses);

    static XElement MultiStatusWithToken(long token, XElement[] responses)
    {
        var ms = MultiStatus(responses);
        ms.Add(new XElement(D + "sync-token", token.ToString(CultureInfo.InvariantCulture)));
        return ms;
    }

    static XElement DeletedResponse(string href) => new(D + "response",
        new XElement(D + "href", href),
        new XElement(D + "status", "HTTP/1.1 404 Not Found"));

    static XElement SupportedReports(params XName[] reports) => new(D + "supported-report-set",
        reports.Select(r => new XElement(D + "supported-report", new XElement(D + "report", new XElement(r)))));

    static XElement Response(string href, params XElement[] props) => new(D + "response",
        new XElement(D + "href", href),
        new XElement(D + "propstat",
            new XElement(D + "prop", props.Cast<object>().ToArray()),
            new XElement(D + "status", "HTTP/1.1 200 OK")));

    static XElement Href(string url) => new(D + "href", url);

    static string Etag(string contentHash) => $"\"{contentHash}\"";

    static async Task<string> ReadBody(HttpContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.Body);
        return await reader.ReadToEndAsync(ctx.RequestAborted);
    }
}
