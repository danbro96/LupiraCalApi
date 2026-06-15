using System.Text.Json.Nodes;
using LupiraCalApi.Api;
using LupiraCalApi.Data;
using Microsoft.EntityFrameworkCore;

namespace LupiraCalApi.Domain;

/// <summary>
/// Cross-domain relations: links from an event/contact to an external reference (e.g. a LupiraTasks item id).
/// References are by string, not FK — the two services own separate databases, so integrity is by convention.
/// </summary>
public sealed class RelationService(CalDbContext db, AccessService access)
{
    public async Task<RelationDto> LinkEventAsync(Guid userId, Guid eventId, CreateRelationRequest r, CancellationToken ct = default)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException("Event not found.");
        if (!await access.CanWriteCalendarAsync(userId, ev.CalendarId, ct)) throw new AccessDeniedException();

        var rel = new Relation
        {
            Id = Guid.NewGuid(), FromKind = "event", FromId = eventId,
            ToKind = r.ToKind, ToRef = r.ToRef, RelationType = r.RelationType,
            Metadata = r.Metadata?.ToJsonString(),
        };
        db.Relations.Add(rel);
        await db.SaveChangesAsync(ct);
        return Map(rel);
    }

    public async Task<List<RelationDto>> ListForEventAsync(Guid userId, Guid eventId, CancellationToken ct = default)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException("Event not found.");
        if (!await access.CanReadCalendarAsync(userId, ev.CalendarId, ct)) throw new AccessDeniedException();

        var rels = await db.Relations.Where(x => x.FromKind == "event" && x.FromId == eventId).ToListAsync(ct);
        return rels.Select(Map).ToList();
    }

    /// <summary>Reverse lookup: events the caller can access that link to a given external reference (e.g. a task).</summary>
    public async Task<List<EventDto>> FindEventsLinkedToAsync(Guid userId, string toKind, string toRef, CancellationToken ct = default)
    {
        var calIds = await access.AccessibleCalendars(userId).Select(c => c.Id).ToListAsync(ct);
        var events = await (
            from rel in db.Relations
            where rel.FromKind == "event" && rel.ToKind == toKind && rel.ToRef == toRef
            join e in db.Events on rel.FromId equals e.Id
            where calIds.Contains(e.CalendarId) && e.DeletedAt == null
            select e).ToListAsync(ct);
        return events.Select(EventService.ToDto).ToList();
    }

    private static RelationDto Map(Relation r) => new(
        r.Id, r.FromKind, r.FromId, r.ToKind, r.ToRef, r.RelationType,
        string.IsNullOrWhiteSpace(r.Metadata) ? null : JsonNode.Parse(r.Metadata));
}
