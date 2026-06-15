using System.Xml.Linq;
using LupiraCalApi.Data;
using LupiraCalApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupiraCalApi.Dav;

/// <summary>
/// Read-only CalDAV (RFC 4791) + CardDAV (RFC 6352) over the shared Postgres model (Phase 3). Writes
/// (PUT/DELETE/MKCALENDAR/PROPPATCH) return 403 until Phase 4. URL layout (all discovered, never typed):
///   /dav/                                  → service root (current-user-principal)
///   /dav/u/{userId}/                       → principal (calendar-home-set, addressbook-home-set)
///   /dav/u/{userId}/cal/                    → calendar home (lists owned calendars)
///   /dav/u/{userId}/cal/{calId}/            → a calendar (lists event resources; REPORT)
///   /dav/u/{userId}/cal/{calId}/{uid}.ics   → an event (GET)
///   /dav/u/{userId}/card/...                → address books + contacts (.vcf)
/// Two-account model for now: a principal sees only the containers it owns (shared-calendar visibility is Phase 7).
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

        if (method == "OPTIONS") { WriteOptions(ctx); return; }

        // Collection-level writes / locking aren't supported (object PUT/DELETE are handled in routing below).
        if (method is "MKCALENDAR" or "MKCOL" or "PROPPATCH" or "MOVE" or "COPY" or "LOCK" or "UNLOCK")
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var db = ctx.RequestServices.GetRequiredService<CalDbContext>();
        var user = await ctx.RequestServices.GetRequiredService<IUserContext>().GetCurrentUserAsync(ctx.RequestAborted);

        var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        var segments = (ctx.Request.Path.Value ?? "").Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        // segments[0] == "dav"
        var rest = segments.Skip(1).ToArray();

        // Only the caller's own principal tree is addressable (two-account model).
        if (rest.Length >= 2 && rest[0] == "u" && rest[1] != user.Id.ToString())
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var depth = ctx.Request.Headers.TryGetValue("Depth", out var dh) ? dh.ToString() : "0";
        var deep = depth is "1" or "infinity";

        // ---- routing ----
        if (rest.Length == 0)                                   // /dav/
        {
            if (method == "PROPFIND") { await WriteMultiStatus(ctx, RootPropfind(baseUrl, user)); return; }
        }
        else if (rest[0] == "u" && rest.Length >= 2)
        {
            if (rest.Length == 2 && method == "PROPFIND") { await WriteMultiStatus(ctx, PrincipalPropfind(baseUrl, user)); return; }

            if (rest.Length >= 3 && rest[2] == "cal")
            {
                if (rest.Length == 3 && method == "PROPFIND") { await WriteMultiStatus(ctx, await CalendarHomePropfind(db, baseUrl, user, deep, ctx.RequestAborted)); return; }
                if (rest.Length == 4)
                {
                    var calId = Guid.Parse(rest[3]);
                    if (method == "PROPFIND") { await WriteMultiStatus(ctx, await CalendarPropfind(db, baseUrl, user, calId, deep, ctx.RequestAborted)); return; }
                    if (method == "REPORT") { await HandleCalendarReport(ctx, db, baseUrl, user, calId); return; }
                }
                if (rest.Length == 5)
                {
                    var calId = Guid.Parse(rest[3]); var uid = StripExt(rest[4]);
                    if (method is "GET" or "HEAD") { await GetEvent(ctx, db, user, calId, uid); return; }
                    if (method == "PUT") { await HandlePutEvent(ctx, user, calId, uid); return; }
                    if (method == "DELETE") { await HandleDeleteEvent(ctx, user, calId, uid); return; }
                }
            }
            else if (rest.Length >= 3 && rest[2] == "card")
            {
                if (rest.Length == 3 && method == "PROPFIND") { await WriteMultiStatus(ctx, await AddressbookHomePropfind(db, baseUrl, user, deep, ctx.RequestAborted)); return; }
                if (rest.Length == 4)
                {
                    var abId = Guid.Parse(rest[3]);
                    if (method == "PROPFIND") { await WriteMultiStatus(ctx, await AddressbookPropfind(db, baseUrl, user, abId, deep, ctx.RequestAborted)); return; }
                    if (method == "REPORT") { await HandleAddressbookReport(ctx, db, baseUrl, user, abId); return; }
                }
                if (rest.Length == 5)
                {
                    var abId = Guid.Parse(rest[3]); var uid = StripExt(rest[4]);
                    if (method is "GET" or "HEAD") { await GetContact(ctx, db, user, abId, uid); return; }
                    if (method == "PUT") { await HandlePutContact(ctx, user, abId, uid); return; }
                    if (method == "DELETE") { await HandleDeleteContact(ctx, user, abId, uid); return; }
                }
            }
        }

        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    // ---------- PROPFIND builders ----------

    static XElement RootPropfind(string baseUrl, User user) => MultiStatus(
        Response($"{baseUrl}/dav/",
            new XElement(D + "resourcetype", new XElement(D + "collection")),
            new XElement(D + "current-user-principal", Href($"{baseUrl}/dav/u/{user.Id}/"))));

    static XElement PrincipalPropfind(string baseUrl, User user) => MultiStatus(
        Response($"{baseUrl}/dav/u/{user.Id}/",
            new XElement(D + "resourcetype", new XElement(D + "collection"), new XElement(D + "principal")),
            new XElement(D + "displayname", user.DisplayName ?? user.Email),
            new XElement(D + "current-user-principal", Href($"{baseUrl}/dav/u/{user.Id}/")),
            new XElement(D + "principal-URL", Href($"{baseUrl}/dav/u/{user.Id}/")),
            new XElement(C + "calendar-home-set", Href($"{baseUrl}/dav/u/{user.Id}/cal/")),
            new XElement(CR + "addressbook-home-set", Href($"{baseUrl}/dav/u/{user.Id}/card/"))));

    static async Task<XElement> CalendarHomePropfind(CalDbContext db, string baseUrl, User user, bool deep, CancellationToken ct)
    {
        var responses = new List<XElement>
        {
            Response($"{baseUrl}/dav/u/{user.Id}/cal/", new XElement(D + "resourcetype", new XElement(D + "collection"))),
        };
        if (deep)
        {
            var cals = await db.Calendars.Where(c => c.OwnerId == user.Id).ToListAsync(ct);
            foreach (var c in cals)
                responses.Add(Response($"{baseUrl}/dav/u/{user.Id}/cal/{c.Id}/", CalendarProps(c)));
        }
        return MultiStatus([.. responses]);
    }

    static async Task<XElement> CalendarPropfind(CalDbContext db, string baseUrl, User user, Guid calId, bool deep, CancellationToken ct)
    {
        var cal = await db.Calendars.FirstOrDefaultAsync(c => c.Id == calId && c.OwnerId == user.Id, ct);
        if (cal is null) return MultiStatus();

        var responses = new List<XElement> { Response($"{baseUrl}/dav/u/{user.Id}/cal/{cal.Id}/", CalendarProps(cal)) };
        if (deep)
        {
            var events = await db.Events.Where(e => e.CalendarId == calId && e.DeletedAt == null).ToListAsync(ct);
            foreach (var e in events)
                responses.Add(Response($"{baseUrl}/dav/u/{user.Id}/cal/{cal.Id}/{e.IcalUid}.ics",
                    new XElement(D + "getetag", Etag(e.ContentHash)),
                    new XElement(D + "getcontenttype", "text/calendar; charset=utf-8")));
        }
        return MultiStatus([.. responses]);
    }

    static XElement[] CalendarProps(Calendar c) =>
    [
        new XElement(D + "resourcetype", new XElement(D + "collection"), new XElement(C + "calendar")),
        new XElement(D + "displayname", c.DisplayName ?? c.Slug),
        new XElement(CS + "getctag", $"\"rev-{c.Revision}\""),
        new XElement(D + "sync-token", $"{c.Revision}"),
        new XElement(C + "supported-calendar-component-set", new XElement(C + "comp", new XAttribute("name", "VEVENT"))),
    ];

    static async Task<XElement> AddressbookHomePropfind(CalDbContext db, string baseUrl, User user, bool deep, CancellationToken ct)
    {
        var responses = new List<XElement>
        {
            Response($"{baseUrl}/dav/u/{user.Id}/card/", new XElement(D + "resourcetype", new XElement(D + "collection"))),
        };
        if (deep)
        {
            var books = await db.AddressBooks.Where(a => a.OwnerId == user.Id).ToListAsync(ct);
            foreach (var a in books)
                responses.Add(Response($"{baseUrl}/dav/u/{user.Id}/card/{a.Id}/", AddressbookProps(a)));
        }
        return MultiStatus([.. responses]);
    }

    static async Task<XElement> AddressbookPropfind(CalDbContext db, string baseUrl, User user, Guid abId, bool deep, CancellationToken ct)
    {
        var book = await db.AddressBooks.FirstOrDefaultAsync(a => a.Id == abId && a.OwnerId == user.Id, ct);
        if (book is null) return MultiStatus();

        var responses = new List<XElement> { Response($"{baseUrl}/dav/u/{user.Id}/card/{book.Id}/", AddressbookProps(book)) };
        if (deep)
        {
            var contacts = await db.Contacts.Where(x => x.AddressBookId == abId && x.DeletedAt == null).ToListAsync(ct);
            foreach (var x in contacts)
                responses.Add(Response($"{baseUrl}/dav/u/{user.Id}/card/{book.Id}/{x.VcardUid}.vcf",
                    new XElement(D + "getetag", Etag(x.ContentHash)),
                    new XElement(D + "getcontenttype", "text/vcard; charset=utf-8")));
        }
        return MultiStatus([.. responses]);
    }

    static XElement[] AddressbookProps(AddressBook a) =>
    [
        new XElement(D + "resourcetype", new XElement(D + "collection"), new XElement(CR + "addressbook")),
        new XElement(D + "displayname", a.DisplayName ?? a.Slug),
        new XElement(CS + "getctag", $"\"rev-{a.Revision}\""),
        new XElement(D + "sync-token", $"{a.Revision}"),
    ];

    // ---------- REPORT (calendar-query / calendar-multiget / addressbook-*) ----------

    static async Task HandleCalendarReport(HttpContext ctx, CalDbContext db, string baseUrl, User user, Guid calId)
    {
        if (!await db.Calendars.AnyAsync(c => c.Id == calId && c.OwnerId == user.Id, ctx.RequestAborted)) { ctx.Response.StatusCode = 404; return; }
        var body = await ReadBody(ctx);
        var requested = ExtractHrefUids(body, ".ics");

        var q = db.Events.Where(e => e.CalendarId == calId && e.DeletedAt == null);
        if (requested.Count > 0) q = q.Where(e => requested.Contains(e.IcalUid));   // calendar-multiget
        var events = await q.ToListAsync(ctx.RequestAborted);                        // calendar-query returns all (Phase 3)

        var responses = events.Select(e => Response($"{baseUrl}/dav/u/{user.Id}/cal/{calId}/{e.IcalUid}.ics",
            new XElement(D + "getetag", Etag(e.ContentHash)),
            new XElement(C + "calendar-data", e.SourceIcalendar)));
        await WriteMultiStatus(ctx, MultiStatus([.. responses]));
    }

    static async Task HandleAddressbookReport(HttpContext ctx, CalDbContext db, string baseUrl, User user, Guid abId)
    {
        if (!await db.AddressBooks.AnyAsync(a => a.Id == abId && a.OwnerId == user.Id, ctx.RequestAborted)) { ctx.Response.StatusCode = 404; return; }
        var body = await ReadBody(ctx);
        var requested = ExtractHrefUids(body, ".vcf");

        var q = db.Contacts.Where(x => x.AddressBookId == abId && x.DeletedAt == null);
        if (requested.Count > 0) q = q.Where(x => requested.Contains(x.VcardUid));
        var contacts = await q.ToListAsync(ctx.RequestAborted);

        var responses = contacts.Select(x => Response($"{baseUrl}/dav/u/{user.Id}/card/{abId}/{x.VcardUid}.vcf",
            new XElement(D + "getetag", Etag(x.ContentHash)),
            new XElement(CR + "address-data", x.SourceVcard)));
        await WriteMultiStatus(ctx, MultiStatus([.. responses]));
    }

    // ---------- GET object ----------

    static async Task GetEvent(HttpContext ctx, CalDbContext db, User user, Guid calId, string icalUid)
    {
        var e = await db.Events.FirstOrDefaultAsync(
            x => x.CalendarId == calId && x.IcalUid == icalUid && x.DeletedAt == null
                 && db.Calendars.Any(c => c.Id == calId && c.OwnerId == user.Id), ctx.RequestAborted);
        if (e is null) { ctx.Response.StatusCode = 404; return; }
        ctx.Response.Headers.ETag = Etag(e.ContentHash);
        ctx.Response.ContentType = "text/calendar; charset=utf-8";
        if (ctx.Request.Method == "HEAD") return;
        await ctx.Response.WriteAsync(e.SourceIcalendar);
    }

    static async Task GetContact(HttpContext ctx, CalDbContext db, User user, Guid abId, string vcardUid)
    {
        var x = await db.Contacts.FirstOrDefaultAsync(
            c => c.AddressBookId == abId && c.VcardUid == vcardUid && c.DeletedAt == null
                 && db.AddressBooks.Any(a => a.Id == abId && a.OwnerId == user.Id), ctx.RequestAborted);
        if (x is null) { ctx.Response.StatusCode = 404; return; }
        ctx.Response.Headers.ETag = Etag(x.ContentHash);
        ctx.Response.ContentType = "text/vcard; charset=utf-8";
        if (ctx.Request.Method == "HEAD") return;
        await ctx.Response.WriteAsync(x.SourceVcard);
    }

    // ---------- write path (Phase 4): object PUT / DELETE with ETag preconditions ----------

    static async Task HandlePutEvent(HttpContext ctx, User user, Guid calId, string uid)
    {
        var raw = await ReadBody(ctx);
        var (ifMatch, ifNoneMatchStar) = Preconditions(ctx);
        try
        {
            var (created, etag) = await ctx.RequestServices.GetRequiredService<EventService>()
                .PutIcsAsync(user.Id, calId, uid, raw, ifMatch, ifNoneMatchStar, ctx.RequestAborted);
            ctx.Response.Headers.ETag = $"\"{etag}\"";
            ctx.Response.StatusCode = created ? StatusCodes.Status201Created : StatusCodes.Status204NoContent;
        }
        catch (DavPreconditionException) { ctx.Response.StatusCode = StatusCodes.Status412PreconditionFailed; }
        catch (FormatException) { ctx.Response.StatusCode = StatusCodes.Status400BadRequest; }
    }

    static async Task HandleDeleteEvent(HttpContext ctx, User user, Guid calId, string uid)
    {
        var (ifMatch, _) = Preconditions(ctx);
        try
        {
            await ctx.RequestServices.GetRequiredService<EventService>().DeleteByUidAsync(user.Id, calId, uid, ifMatch, ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }
        catch (DavPreconditionException) { ctx.Response.StatusCode = StatusCodes.Status412PreconditionFailed; }
    }

    static async Task HandlePutContact(HttpContext ctx, User user, Guid abId, string uid)
    {
        var raw = await ReadBody(ctx);
        var (ifMatch, ifNoneMatchStar) = Preconditions(ctx);
        try
        {
            var (created, etag) = await ctx.RequestServices.GetRequiredService<ContactService>()
                .PutVcfAsync(user.Id, abId, uid, raw, ifMatch, ifNoneMatchStar, ctx.RequestAborted);
            ctx.Response.Headers.ETag = $"\"{etag}\"";
            ctx.Response.StatusCode = created ? StatusCodes.Status201Created : StatusCodes.Status204NoContent;
        }
        catch (DavPreconditionException) { ctx.Response.StatusCode = StatusCodes.Status412PreconditionFailed; }
        catch (FormatException) { ctx.Response.StatusCode = StatusCodes.Status400BadRequest; }
    }

    static async Task HandleDeleteContact(HttpContext ctx, User user, Guid abId, string uid)
    {
        var (ifMatch, _) = Preconditions(ctx);
        try
        {
            await ctx.RequestServices.GetRequiredService<ContactService>().DeleteByUidAsync(user.Id, abId, uid, ifMatch, ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }
        catch (DavPreconditionException) { ctx.Response.StatusCode = StatusCodes.Status412PreconditionFailed; }
    }

    static (string? IfMatch, bool IfNoneMatchStar) Preconditions(HttpContext ctx)
    {
        string? ifMatch = null;
        if (ctx.Request.Headers.TryGetValue("If-Match", out var im) && im.Count > 0)
        {
            var v = im.ToString().Trim();
            if (v.Length > 0 && v != "*") ifMatch = v.Trim('"');
        }
        var inm = ctx.Request.Headers.TryGetValue("If-None-Match", out var n) ? n.ToString().Trim() : "";
        return (ifMatch, inm == "*");
    }

    // ---------- helpers ----------

    static void WriteOptions(HttpContext ctx)
    {
        ctx.Response.Headers["DAV"] = "1, 2, 3, calendar-access, addressbook";
        ctx.Response.Headers["Allow"] = "OPTIONS, GET, HEAD, PROPFIND, REPORT";
        ctx.Response.StatusCode = StatusCodes.Status200OK;
    }

    static async Task WriteMultiStatus(HttpContext ctx, XElement multistatus)
    {
        ctx.Response.StatusCode = 207;   // Multi-Status
        ctx.Response.ContentType = "application/xml; charset=utf-8";
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), multistatus);
        await ctx.Response.WriteAsync(doc.Declaration + "\n" + doc.ToString(SaveOptions.DisableFormatting));
    }

    static XElement MultiStatus(params XElement[] responses) => new(D + "multistatus",
        new XAttribute(XNamespace.Xmlns + "d", D.NamespaceName),
        new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName),
        new XAttribute(XNamespace.Xmlns + "cr", CR.NamespaceName),
        new XAttribute(XNamespace.Xmlns + "cs", CS.NamespaceName),
        responses);

    static XElement Response(string href, params XElement[] props) => new(D + "response",
        new XElement(D + "href", href),
        new XElement(D + "propstat",
            new XElement(D + "prop", props.Cast<object>().ToArray()),
            new XElement(D + "status", "HTTP/1.1 200 OK")));

    static XElement Href(string url) => new(D + "href", url);

    static string Etag(string contentHash) => $"\"{contentHash}\"";

    static string StripExt(string file)
    {
        var dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    static async Task<string> ReadBody(HttpContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.Body);
        return await reader.ReadToEndAsync(ctx.RequestAborted);
    }

    /// <summary>Pulls the resource UIDs out of multiget &lt;D:href&gt; entries (the {uid}.ics / {uid}.vcf tail).</summary>
    static List<string> ExtractHrefUids(string body, string ext)
    {
        var uids = new HashSet<string>();
        if (string.IsNullOrWhiteSpace(body)) return [];
        try
        {
            var doc = XDocument.Parse(body);
            foreach (var href in doc.Descendants(D + "href"))
            {
                var name = href.Value.TrimEnd('/');
                var slash = name.LastIndexOf('/');
                if (slash >= 0) name = name[(slash + 1)..];
                if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name = name[..^ext.Length];
                if (name.Length > 0) uids.Add(name);
            }
        }
        catch { /* malformed body → treat as query (return all) */ }
        return [.. uids];
    }
}
