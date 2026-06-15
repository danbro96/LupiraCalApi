namespace LupiraCalApi.Dav;

/// <summary>
/// Single entry point for the CalDAV/CardDAV surface. Because PROPFIND/REPORT/PROPPATCH/MKCALENDAR aren't
/// standard minimal-API verbs, /dav is a catch-all that dispatches on the HTTP method.
///
/// Phase 3+ implements, in order of client need: OPTIONS (+ DAV header), .well-known redirects, PROPFIND
/// (current-user-principal → home-set → collection enum), REPORT (calendar-query / addressbook-query /
/// multiget / sync-collection), GET/PUT/DELETE with ETag + If-Match. Hrefs must be absolute
/// (https://cal.lupira.com/...) — UseForwardedHeaders in Program.cs makes that work behind the tunnel.
/// </summary>
public static class DavRouter
{
    public static async Task Handle(HttpContext ctx)
    {
        // TODO(Phase 3): switch on ctx.Request.Method and build 207 Multi-Status responses (System.Xml.Linq).
        ctx.Response.StatusCode = StatusCodes.Status501NotImplemented;
        ctx.Response.ContentType = "text/plain";
        await ctx.Response.WriteAsync($"CalDAV/CardDAV not implemented yet (method {ctx.Request.Method}, Phase 3).");
    }
}
