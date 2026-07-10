using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Serialization;
using Marten;
using DavCalendar = LupiraCalApi.Domain.Calendar;   // disambiguate from System.Globalization.Calendar

namespace LupiraCalApi.Dav;

/// <summary>
/// The calendar (VEVENT) half of the internal /dav-backend contract consumed by the LupiraDavApi gateway.
/// Acts on behalf of the principal named by the path {email} (the gateway verified the human credential via
/// LDAP Basic auth); JIT-provisions the principal and its standard calendar set on first sight, mirroring
/// the old first-PROPFIND self-provision behavior. Only Agenda-class calendars are DAV-projected; only
/// items accepted into the calendar are exposed. ICS is regenerated from the snapshot (denormalized
/// location label) — DAV never depends on a stored blob.
/// </summary>
public sealed class DavBackendHandler(
    IQuerySession session,
    AccessResolver access,
    PrincipalDirectory principals,
    CalendarService calendars,
    CalendarItemService items,
    DavChangeFeed feed,
    TimeRangeFilter timeRange)
{
    public async Task<IResult> CollectionsAsync(string email, CancellationToken ct)
    {
        var principal = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        // A DAV-only principal has no containers yet — seed the standard set on first contact.
        if ((await access.AccessibleCalendarIdsAsync(principal.Id, ct)).Count == 0)
            await calendars.BootstrapPersonalAsync(principal.Id, ct);

        var ids = await access.AccessibleCalendarIdsAsync(principal.Id, ct);
        // Only agenda calendars are DAV-projected; system calendars (Inbox/LlmPrompts/UserCheckIn/DevOps) are REST/DB-only.
        var cals = await session.Query<DavCalendar>().Where(c => ids.Contains(c.Id) && c.Class == CalendarClass.Agenda).ToListAsync(ct);
        var token = await feed.CurrentTokenAsync(ct);
        return TypedResults.Ok(new DavCollectionsDto
        {
            Principal = new DavPrincipalDto { DisplayName = principal.DisplayName ?? principal.Email },
            Collections = [.. cals.Select(c => new DavCollectionDto
            {
                Id = c.Id,
                Kind = DavCollectionKind.EventCalendar,
                DisplayName = c.DisplayName ?? c.Slug,
                Ctag = $"seq-{token}",
                SyncToken = token.ToString(),
            })],
        });
    }

    public async Task<IResult> QueryAsync(string email, Guid collectionId, DavQueryRequest body, CancellationToken ct)
    {
        var principal = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        if (!await IsReadableAgendaAsync(principal.Id, collectionId, ct)) return TypedResults.NotFound();

        var live = await feed.AcceptedItemsAsync(collectionId, ct);
        IEnumerable<CalendarItem> selected = live;
        if (body.Uids is { Count: > 0 } uids)
        {
            var set = uids.ToHashSet(StringComparer.Ordinal);
            selected = selected.Where(i => set.Contains(i.ExternalId));
        }
        if (body is { Start: { } start, End: { } end })
            selected = selected.Where(i => timeRange.Overlaps(i, start, end));   // recurrence expansion stays in this domain

        return TypedResults.Ok(new DavResourcesDto
        {
            Resources = [.. selected.Select(i => new DavResourceDto
            {
                Uid = i.ExternalId,
                Etag = i.ContentHash,
                Content = body.IncludeContent ? ICalSerializer.From(i, i.LocationLabel) : null,
            })],
        });
    }

    public async Task<IResult> GetResourceAsync(string email, Guid collectionId, string uid, HttpContext ctx, CancellationToken ct)
    {
        var principal = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        if (!await IsReadableAgendaAsync(principal.Id, collectionId, ct)) return TypedResults.NotFound();

        var item = await session.LoadAsync<CalendarItem>(DeterministicGuid.From(uid), ct);
        if (item is null || item.DeletedAt is not null || !item.IsAcceptedIn(collectionId)) return TypedResults.NotFound();

        ctx.Response.Headers.ETag = $"\"{item.ContentHash}\"";
        return TypedResults.Text(ICalSerializer.From(item, item.LocationLabel), "text/calendar; charset=utf-8");
    }

    public async Task<IResult> PutResourceAsync(string email, Guid collectionId, string uid, HttpContext ctx, CancellationToken ct)
    {
        var principal = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        using var reader = new StreamReader(ctx.Request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        var (ifMatch, ifNoneMatchStar) = ParsePreconditions(ctx.Request.Headers.IfMatch, ctx.Request.Headers.IfNoneMatch);

        var result = await items.PutIcsAsync(principal.Id, collectionId, uid, raw, ifMatch, ifNoneMatchStar, ct);
        if (result.Status == OpStatus.Ok && result.Value is { } w)
        {
            ctx.Response.Headers.ETag = $"\"{w.Etag}\"";
            return TypedResults.StatusCode(w.Created ? StatusCodes.Status201Created : StatusCodes.Status204NoContent);
        }
        return TypedResults.StatusCode(DavStatus(result.Status));
    }

    public async Task<IResult> DeleteResourceAsync(string email, Guid collectionId, string uid, HttpContext ctx, CancellationToken ct)
    {
        var principal = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        var (ifMatch, _) = ParsePreconditions(ctx.Request.Headers.IfMatch, ctx.Request.Headers.IfNoneMatch);
        var result = await items.DeleteByUidAsync(principal.Id, collectionId, uid, ifMatch, ct);
        return TypedResults.StatusCode(DavStatus(result.Status));
    }

    public async Task<IResult> ChangesAsync(string email, Guid collectionId, string? since, CancellationToken ct)
    {
        var principal = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        if (!await IsReadableAgendaAsync(principal.Id, collectionId, ct)) return TypedResults.NotFound();

        // An unparsable/absent token degrades to the full live listing — self-healing resync.
        long? parsed = long.TryParse(since, out var t) ? t : null;
        var (token, changes) = await feed.ChangesSinceAsync(collectionId, parsed, ct);
        return TypedResults.Ok(new DavChangesDto
        {
            SyncToken = token.ToString(),
            Changed = [.. changes.Where(c => !c.Deleted).Select(c => new DavChangeDto { Uid = c.Uid, Etag = c.Etag! })],
            Deleted = [.. changes.Where(c => c.Deleted).Select(c => c.Uid)],
        });
    }

    /// <summary>Read gate for the DAV projection: the caller can read the calendar AND it is Agenda-class
    /// (system calendars — Inbox/LlmPrompts/UserCheckIn/DevOps — are REST/DB-only, opaque 404 here).</summary>
    private async Task<bool> IsReadableAgendaAsync(Guid principalId, Guid collectionId, CancellationToken ct)
    {
        if (!await access.CanReadCalendarAsync(principalId, collectionId, ct)) return false;
        var cal = await session.LoadAsync<DavCalendar>(collectionId, ct);
        return cal is { Class: CalendarClass.Agenda };
    }

    /// <summary>An <c>If-Match</c> of <c>*</c> (or empty) is "no specific tag"; quotes are stripped from a
    /// concrete tag. <c>If-None-Match: *</c> is the "create only if absent" guard.</summary>
    internal static (string? IfMatch, bool IfNoneMatchStar) ParsePreconditions(string? ifMatchHeader, string? ifNoneMatchHeader)
    {
        string? ifMatch = null;
        var im = ifMatchHeader?.Trim();
        if (!string.IsNullOrEmpty(im) && im != "*") ifMatch = im.Trim('"');
        var inm = ifNoneMatchHeader?.Trim() ?? "";
        return (ifMatch, inm == "*");
    }

    internal static int DavStatus(OpStatus status) => status switch
    {
        OpStatus.Ok => StatusCodes.Status204NoContent,
        OpStatus.Forbidden => StatusCodes.Status403Forbidden,
        OpStatus.NotFound => StatusCodes.Status404NotFound,
        OpStatus.Conflict => StatusCodes.Status412PreconditionFailed,
        OpStatus.Invalid => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError,
    };
}
