using LupiraCalApi.Auth;
using LupiraCalApi.Data;
using LupiraCalApi.Dtos.Events;
using LupiraCalApi.Dtos.Relations;
using LupiraCalApi.Mappers;
using Microsoft.EntityFrameworkCore;

namespace LupiraCalApi.Application;

/// <summary>
/// Cross-domain relations: links from an event/contact to an external reference (e.g. a LupiraTasks item id).
/// References are by string, not FK — the two services own separate databases, so integrity is by convention.
/// </summary>
public sealed class RelationService(CalDbContext db, AccessResolver access)
{
    public async Task<OpResult<RelationDto>> LinkEventAsync(Guid userId, Guid eventId, CreateRelationRequest r, CancellationToken ct = default)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.DeletedAt == null, ct);
        if (ev is null) return OpResult<RelationDto>.NotFound();
        if (!await access.CanWriteCalendarAsync(userId, ev.CalendarId, ct)) return OpResult<RelationDto>.Forbidden("No write access to this event.");

        var rel = new Data.Entities.Relation
        {
            Id = Guid.NewGuid(),
            FromKind = "event",
            FromId = eventId,
            ToKind = r.ToKind,
            ToRef = r.ToRef,
            RelationType = r.RelationType,
            Metadata = r.Metadata?.ToJsonString(),
        };
        db.Relations.Add(rel);
        await db.SaveChangesAsync(ct);
        return OpResult<RelationDto>.Ok(rel.ToResponse());
    }

    public async Task<OpResult<List<RelationDto>>> ListForEventAsync(Guid userId, Guid eventId, CancellationToken ct = default)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.DeletedAt == null, ct);
        if (ev is null) return OpResult<List<RelationDto>>.NotFound();
        if (!await access.CanReadCalendarAsync(userId, ev.CalendarId, ct)) return OpResult<List<RelationDto>>.Forbidden("No access to this event.");

        var rels = await db.Relations.Where(x => x.FromKind == "event" && x.FromId == eventId).ToListAsync(ct);
        return OpResult<List<RelationDto>>.Ok(rels.Select(RelationMapper.ToResponse).ToList());
    }

    /// <summary>Reverse lookup: events the caller can access that link to a given external reference (e.g. a task).</summary>
    public async Task<OpResult<List<EventDto>>> FindEventsLinkedToAsync(Guid userId, string toKind, string toRef, CancellationToken ct = default)
    {
        var calIds = await access.AccessibleCalendars(userId).Select(c => c.Id).ToListAsync(ct);
        var events = await (
            from rel in db.Relations
            where rel.FromKind == "event" && rel.ToKind == toKind && rel.ToRef == toRef
            join e in db.Events on rel.FromId equals e.Id
            where calIds.Contains(e.CalendarId) && e.DeletedAt == null
            select e).ToListAsync(ct);
        return OpResult<List<EventDto>>.Ok(events.Select(EventMapper.ToResponse).ToList());
    }
}
