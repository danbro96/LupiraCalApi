using System.Text.Json.Nodes;
using LupiraCalApi.Auth;
using LupiraCalApi.Data;
using LupiraCalApi.Data.Entities;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Events;
using LupiraCalApi.Mappers;
using LupiraCalApi.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LupiraCalApi.Application;

/// <summary>
/// The calendar core shared by REST, DAV, and MCP. Every mutation authors the canonical
/// <c>source_icalendar</c>, recomputes <c>content_hash</c>, bumps the owning calendar's revision, and
/// appends a change-log row (tombstone for deletes) so the CalDAV sync surface has everything it needs.
/// </summary>
public sealed class EventService(CalDbContext db, AccessResolver access, RecurrenceExpander expander)
{
    public async Task<OpResult<EventDto>> CreateAsync(Guid userId, CreateEventRequest r, CancellationToken ct = default)
    {
        if (!await access.CanWriteCalendarAsync(userId, r.CalendarId, ct)) return OpResult<EventDto>.Forbidden("No write access to this calendar.");

        var e = new Event
        {
            Id = Guid.NewGuid(),
            CalendarId = r.CalendarId,
            IcalUid = $"{Guid.NewGuid():N}@cal.lupira.com",
            Title = r.Title, Description = r.Description, Location = r.Location, Status = r.Status,
            IsAllDay = r.IsAllDay, StartsAt = r.StartsAt, EndsAt = r.EndsAt, StartTimezone = r.StartTimezone,
            StartDate = r.StartDate, EndDate = r.EndDate, RecurrenceRule = r.RecurrenceRule, Tags = r.Tags,
            Metadata = "{}",
        };
        e.SourceIcalendar = ICalSerializer.ToICalendar(e);
        e.ContentHash = ContentHash.Of(e.SourceIcalendar);

        db.Events.Add(e);
        await BumpAndLogAsync(e.CalendarId, e.IcalUid, "saved", e.ContentHash, ct);
        await db.SaveChangesAsync(ct);
        return OpResult<EventDto>.Ok(e.ToResponse());
    }

    public async Task<OpResult<List<EventOccurrenceDto>>> SearchAsync(
        Guid userId, string? query, DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId,
        string? tag = null, string? metadataContains = null, CancellationToken ct = default)
    {
        var calIds = await access.AccessibleCalendars(userId).Select(c => c.Id).ToListAsync(ct);
        if (calendarId is { } cid)
        {
            if (!calIds.Contains(cid)) return OpResult<List<EventOccurrenceDto>>.Forbidden("No access to this calendar.");
            calIds = [cid];
        }

        var q = db.Events.Where(e => calIds.Contains(e.CalendarId) && e.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(e => e.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("english", term))
                || EF.Functions.TrigramsWordSimilarity(term, e.Title ?? "") >= 0.3);
        }
        if (!string.IsNullOrWhiteSpace(tag))
            q = q.Where(e => e.Tags != null && e.Tags.Contains(tag));
        if (!string.IsNullOrWhiteSpace(metadataContains))
            q = q.Where(e => EF.Functions.JsonContains(e.Metadata, metadataContains));
        var candidates = await q.ToListAsync(ct);

        var windowStart = from ?? DateTimeOffset.UtcNow.AddYears(-1);
        var windowEnd = to ?? DateTimeOffset.UtcNow.AddYears(1);
        var results = new List<EventOccurrenceDto>();

        foreach (var e in candidates)
        {
            TimeSpan? duration = (e.StartsAt is { } s && e.EndsAt is { } en) ? en - s : null;

            if (!string.IsNullOrWhiteSpace(e.RecurrenceRule))
            {
                foreach (var occ in expander.Expand(e.SourceIcalendar, windowStart, windowEnd))
                    results.Add(new EventOccurrenceDto(e.Id, e.CalendarId, e.Title, e.Location, e.IsAllDay,
                        occ, duration is { } d ? occ + d : null, e.ContentHash));
            }
            else if (OccurrenceStart(e) is { } start && start >= windowStart && start < windowEnd)
            {
                results.Add(new EventOccurrenceDto(e.Id, e.CalendarId, e.Title, e.Location, e.IsAllDay,
                    start, duration is { } d ? start + d : e.EndsAt, e.ContentHash));
            }
        }
        return OpResult<List<EventOccurrenceDto>>.Ok([.. results.OrderBy(r => r.Start)]);
    }

    public async Task<OpResult<EventDto>> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var e = await db.Events.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
        if (e is null) return OpResult<EventDto>.NotFound();
        if (!await access.CanReadCalendarAsync(userId, e.CalendarId, ct)) return OpResult<EventDto>.Forbidden("No access to this event.");
        return OpResult<EventDto>.Ok(e.ToResponse());
    }

    public async Task<OpResult<EventDto>> UpdateAsync(Guid userId, Guid id, UpdateEventRequest r, CancellationToken ct = default)
    {
        var e = await db.Events.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
        if (e is null) return OpResult<EventDto>.NotFound();
        if (!await access.CanWriteCalendarAsync(userId, e.CalendarId, ct)) return OpResult<EventDto>.Forbidden("No write access to this event.");

        if (r.Title is not null) e.Title = r.Title;
        if (r.Description is not null) e.Description = r.Description;
        if (r.Location is not null) e.Location = r.Location;
        if (r.Status is not null) e.Status = r.Status;
        if (r.StartsAt is not null) e.StartsAt = r.StartsAt;
        if (r.EndsAt is not null) e.EndsAt = r.EndsAt;
        if (r.RecurrenceRule is not null) e.RecurrenceRule = r.RecurrenceRule;
        if (r.Tags is not null) e.Tags = r.Tags;

        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.SourceIcalendar = ICalSerializer.ToICalendar(e);
        e.ContentHash = ContentHash.Of(e.SourceIcalendar);
        await BumpAndLogAsync(e.CalendarId, e.IcalUid, "saved", e.ContentHash, ct);
        await db.SaveChangesAsync(ct);
        return OpResult<EventDto>.Ok(e.ToResponse());
    }

    public async Task<OpResult> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var e = await db.Events.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
        if (e is null) return OpResult.NotFound();
        if (!await access.CanWriteCalendarAsync(userId, e.CalendarId, ct)) return OpResult.Forbidden("No write access to this event.");

        e.DeletedAt = DateTimeOffset.UtcNow;
        await BumpAndLogAsync(e.CalendarId, e.IcalUid, "deleted", null, ct);
        await db.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    public async Task<OpResult<EventDto>> AttachMetadataAsync(Guid userId, Guid id, JsonNode patch, CancellationToken ct = default)
    {
        var e = await db.Events.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
        if (e is null) return OpResult<EventDto>.NotFound();
        if (!await access.CanWriteCalendarAsync(userId, e.CalendarId, ct)) return OpResult<EventDto>.Forbidden("No write access to this event.");

        var current = (JsonNode.Parse(string.IsNullOrWhiteSpace(e.Metadata) ? "{}" : e.Metadata) as JsonObject) ?? new JsonObject();
        if (patch is JsonObject obj)
            foreach (var kv in obj)
                current[kv.Key] = kv.Value?.DeepClone();

        e.Metadata = current.ToJsonString();
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return OpResult<EventDto>.Ok(e.ToResponse());
    }

    // ---- CalDAV write path: upsert/delete a resource keyed by its URL uid, with ETag preconditions ----

    public async Task<OpResult<DavWriteResult>> PutIcsAsync(
        Guid userId, Guid calendarId, string icalUid, string rawIcs, string? ifMatch, bool ifNoneMatchStar, CancellationToken ct = default)
    {
        if (!await access.CanWriteCalendarAsync(userId, calendarId, ct)) return OpResult<DavWriteResult>.Forbidden("No write access to this calendar.");

        // Find any row for this uid (incl. soft-deleted) so a DELETE-then-PUT resurrects it rather than
        // colliding with the unique (calendar_id, ical_uid) index. "Exists" for preconditions = live row.
        var existing = await db.Events.FirstOrDefaultAsync(e => e.CalendarId == calendarId && e.IcalUid == icalUid, ct);
        var live = existing is { DeletedAt: null };
        if (ifNoneMatchStar && live) return OpResult<DavWriteResult>.Conflict("Resource already exists.");
        if (ifMatch is not null && (!live || existing!.ContentHash != ifMatch)) return OpResult<DavWriteResult>.Conflict("ETag mismatch.");

        ParsedEvent p;
        try { p = ICalSerializer.ParseICalendar(rawIcs); }
        catch (FormatException ex) { return OpResult<DavWriteResult>.Invalid(ex.Message); }

        var e = existing ?? new Event { Id = Guid.NewGuid(), CalendarId = calendarId, IcalUid = icalUid, Metadata = "{}" };
        e.Title = p.Title; e.Description = p.Description; e.Location = p.Location;
        e.IsAllDay = p.IsAllDay; e.StartsAt = p.StartsAt; e.EndsAt = p.EndsAt;
        e.StartTimezone = p.StartTimezone; e.EndTimezone = p.EndTimezone;
        e.StartDate = p.StartDate; e.EndDate = p.EndDate; e.RecurrenceRule = p.RecurrenceRule;
        e.SourceIcalendar = rawIcs;                       // dual representation: store the client blob verbatim
        e.ContentHash = ContentHash.Of(rawIcs);
        e.DeletedAt = null;                               // resurrect if this uid was previously deleted

        if (existing is null) db.Events.Add(e);
        else e.UpdatedAt = DateTimeOffset.UtcNow;

        await BumpAndLogAsync(calendarId, icalUid, "saved", e.ContentHash, ct);
        await db.SaveChangesAsync(ct);
        return OpResult<DavWriteResult>.Ok(new DavWriteResult(!live, e.ContentHash));
    }

    public async Task<OpResult> DeleteByUidAsync(Guid userId, Guid calendarId, string icalUid, string? ifMatch, CancellationToken ct = default)
    {
        var e = await db.Events.FirstOrDefaultAsync(x => x.CalendarId == calendarId && x.IcalUid == icalUid && x.DeletedAt == null, ct);
        if (e is null) return OpResult.NotFound();
        if (!await access.CanWriteCalendarAsync(userId, calendarId, ct)) return OpResult.Forbidden("No write access to this calendar.");
        if (ifMatch is not null && e.ContentHash != ifMatch) return OpResult.Conflict("ETag mismatch.");

        e.DeletedAt = DateTimeOffset.UtcNow;
        await BumpAndLogAsync(calendarId, icalUid, "deleted", null, ct);
        await db.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    private async Task BumpAndLogAsync(Guid calendarId, string icalUid, string changeType, string? hash, CancellationToken ct)
    {
        var cal = await db.Calendars.FirstAsync(c => c.Id == calendarId, ct);
        cal.Revision += 1;
        cal.UpdatedAt = DateTimeOffset.UtcNow;
        db.CalendarChanges.Add(new CalendarChange
        {
            CalendarId = calendarId, Revision = cal.Revision,
            ItemIcalUid = icalUid, ChangeType = changeType, ContentHash = hash,
        });
    }

    private static DateTimeOffset? OccurrenceStart(Event e)
    {
        if (e.IsAllDay && e.StartDate is { } d) return new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero);
        return e.StartsAt;
    }
}
