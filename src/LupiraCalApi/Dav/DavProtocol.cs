using LupiraCalApi.Application;
using LupiraCalApi.Domain;
using System.Globalization;
using System.Xml.Linq;

namespace LupiraCalApi.Dav;

/// <summary>
/// The pure (no HttpContext, no Marten) protocol logic behind <see cref="DavRouter"/>: request-body parsing,
/// ETag-precondition parsing, time-range overlap math, and status mapping. Split out so the fiddly edge cases
/// (all-day boundaries, malformed tokens, hostile XML) are unit-testable without the full HTTP+DB stack.
/// </summary>
internal static class DavProtocol
{
    static readonly XNamespace D = "DAV:";

    // ---- request-body parsing ----

    public static XDocument? TryParseXml(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return XDocument.Parse(body); } catch { return null; }
    }

    public static long? ParseSyncToken(XDocument doc)
    {
        var el = doc.Descendants(D + "sync-token").FirstOrDefault();
        var v = el?.Value.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : null;
    }

    public static (DateTimeOffset Start, DateTimeOffset End)? ParseTimeRange(XDocument? doc)
    {
        var tr = doc?.Descendants().FirstOrDefault(x => x.Name.LocalName == "time-range");
        if (tr is null) return null;
        var s = ParseICalUtc(tr.Attribute("start")?.Value);
        var e = ParseICalUtc(tr.Attribute("end")?.Value);
        return s is { } start && e is { } end ? (start, end) : null;
    }

    public static DateTimeOffset? ParseICalUtc(string? s) =>
        !string.IsNullOrEmpty(s) && DateTimeOffset.TryParseExact(
            s, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

    public static List<string> ExtractHrefUids(string body, string ext)
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
        catch { /* malformed → treat as query (return all) */ }
        return [.. uids];
    }

    public static string StripExt(string file)
    {
        var dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    // ---- ETag preconditions ----

    /// <summary>Parses the raw <c>If-Match</c> / <c>If-None-Match</c> header values. An <c>If-Match</c> of <c>*</c>
    /// (or empty) is treated as "no specific tag"; quotes are stripped from a concrete tag. Returns whether
    /// <c>If-None-Match</c> is the <c>*</c> wildcard (the "create only if absent" guard).</summary>
    public static (string? IfMatch, bool IfNoneMatchStar) ParsePreconditions(string? ifMatchHeader, string? ifNoneMatchHeader)
    {
        string? ifMatch = null;
        var im = ifMatchHeader?.Trim();
        if (!string.IsNullOrEmpty(im) && im != "*") ifMatch = im.Trim('"');
        var inm = ifNoneMatchHeader?.Trim() ?? "";
        return (ifMatch, inm == "*");
    }

    // ---- time-range overlap (half-open: [start, end)) ----

    public static bool OverlapsWindow(CalendarItem i, DateTimeOffset start, DateTimeOffset end, RecurrenceExpander exp)
    {
        if (!string.IsNullOrWhiteSpace(i.RecurrenceRule)) return exp.Expand(i, start, end).Count > 0;
        DateTimeOffset? s = i.IsAllDay && i.StartDate is { } d
            ? new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero) : i.StartsAt;
        if (s is null) return false;
        var en = i.EndsAt ?? (i.IsAllDay && i.EndDate is { } ed
            ? new DateTimeOffset(ed.Year, ed.Month, ed.Day, 0, 0, 0, TimeSpan.Zero) : s.Value);
        return s.Value < end && en >= start;
    }

    // ---- status mapping ----

    public static int DavStatus(OpStatus status) => status switch
    {
        OpStatus.Ok => StatusCodes.Status204NoContent,
        OpStatus.Forbidden => StatusCodes.Status403Forbidden,
        OpStatus.NotFound => StatusCodes.Status404NotFound,
        OpStatus.Conflict => StatusCodes.Status412PreconditionFailed,
        OpStatus.Invalid => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError,
    };
}
