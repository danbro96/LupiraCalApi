using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Relations;
using LupiraCalApi.Mappers;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>
/// Cross-API relations: a by-reference link from a calendar item to an external reference (e.g. a LupiraTasks item,
/// or an Activity-API engagement/project). References are by string, not FK — integrity is by convention.
/// </summary>
public sealed class RelationService(IDocumentSession session, AccessResolver access)
{
    public async Task<OpResult<RelationDto>> LinkItemAsync(Guid principalId, Guid itemId, CreateRelationRequest r, CancellationToken ct = default)
    {
        var item = await session.LoadAsync<CalendarItem>(itemId, ct);
        if (item is null || item.DeletedAt is not null) return OpResult<RelationDto>.NotFound();
        if (!await access.CanWriteItemAsync(principalId, item, ct)) return OpResult<RelationDto>.Forbidden("No write access to this item.");

        var rel = new Relation
        {
            Id = Guid.NewGuid(),
            FromKind = "item",
            FromId = itemId,
            ToKind = r.ToKind,
            ToRef = r.ToRef,
            RelationType = r.RelationType,
            Metadata = r.Metadata?.ToJsonString(),
        };
        session.Store(rel);
        await session.SaveChangesAsync(ct);
        return OpResult<RelationDto>.Ok(rel.ToResponse());
    }

    public async Task<OpResult<List<RelationDto>>> ListForItemAsync(Guid principalId, Guid itemId, CancellationToken ct = default)
    {
        var item = await session.LoadAsync<CalendarItem>(itemId, ct);
        if (item is null || item.DeletedAt is not null) return OpResult<List<RelationDto>>.NotFound();
        if (!await access.CanReadItemAsync(principalId, item, ct)) return OpResult<List<RelationDto>>.Forbidden("No access to this item.");
        var rels = await session.Query<Relation>().Where(x => x.FromKind == "item" && x.FromId == itemId).ToListAsync(ct);
        return OpResult<List<RelationDto>>.Ok(rels.Select(RelationMapper.ToResponse).ToList());
    }

    /// <summary>Reverse lookup: items the caller can access that link to a given external reference.</summary>
    public async Task<OpResult<List<CalendarItemDto>>> FindItemsLinkedToAsync(Guid principalId, string toKind, string toRef, CancellationToken ct = default)
    {
        var rels = await session.Query<Relation>().Where(x => x.FromKind == "item" && x.ToKind == toKind && x.ToRef == toRef).ToListAsync(ct);
        var ids = rels.Select(r => r.FromId).Distinct().ToList();
        var items = await session.Query<CalendarItem>().Where(i => ids.Contains(i.Id) && i.DeletedAt == null).ToListAsync(ct);
        var calIds = await access.AccessibleCalendarIdsAsync(principalId, ct);
        var visible = items.Where(i => i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && calIds.Contains(m.CalendarId)));
        return OpResult<List<CalendarItemDto>>.Ok(visible.Select(i => i.ToResponse()).ToList());
    }
}
