using Lupira.Results;
using LupiraCalApi.Core.Auth;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Relations;
using LupiraCalApi.Core.Mappers;
using Marten;

namespace LupiraCalApi.Core.Application.Items;

/// <summary>
/// Cross-API relations: a by-reference link from a calendar item to an external reference (e.g. a LupiraTasks item,
/// or an Activity-API engagement/project). References are by string, not FK — integrity is by convention.
/// </summary>
public sealed class RelationService(IDocumentSession session, AccessResolver access, CompletenessResolver completeness)
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

    public const int BatchMax = 500;

    /// <summary>Idempotent per (item, kind, ref, type): references already linked are skipped, so a retried or
    /// overlapping batch never duplicates an edge. Returns every edge of that kind and type on the item.</summary>
    public async Task<OpResult<List<RelationDto>>> LinkItemBatchAsync(
        Guid principalId, Guid itemId, CreateRelationsBatchRequest r, CancellationToken ct = default)
    {
        if (r.ToRefs.Count > BatchMax) return OpResult<List<RelationDto>>.Invalid($"At most {BatchMax} references per batch.");
        var item = await session.LoadAsync<CalendarItem>(itemId, ct);
        if (item is null || item.DeletedAt is not null) return OpResult<List<RelationDto>>.NotFound();
        if (!await access.CanWriteItemAsync(principalId, item, ct)) return OpResult<List<RelationDto>>.Forbidden("No write access to this item.");

        var existing = await session.Query<Relation>()
            .Where(x => x.FromKind == "item" && x.FromId == itemId && x.ToKind == r.ToKind && x.RelationType == r.RelationType)
            .ToListAsync(ct);
        var known = existing.Select(x => x.ToRef).ToHashSet(StringComparer.Ordinal);
        var added = r.ToRefs.Where(known.Add).Select(toRef => new Relation
        {
            Id = Guid.NewGuid(),
            FromKind = "item",
            FromId = itemId,
            ToKind = r.ToKind,
            ToRef = toRef,
            RelationType = r.RelationType,
        }).ToList();

        if (added.Count > 0)
        {
            session.Store(added.ToArray());
            await session.SaveChangesAsync(ct);
        }

        return OpResult<List<RelationDto>>.Ok(existing.Concat(added).Select(RelationMapper.ToResponse).ToList());
    }

    public async Task<OpResult> UnlinkItemAsync(Guid principalId, Guid itemId, Guid relationId, CancellationToken ct = default)
    {
        var item = await session.LoadAsync<CalendarItem>(itemId, ct);
        if (item is null || item.DeletedAt is not null) return OpResult.NotFound();
        if (!await access.CanWriteItemAsync(principalId, item, ct)) return OpResult.Forbidden("No write access to this item.");

        var rel = await session.LoadAsync<Relation>(relationId, ct);
        if (rel is null || rel.FromKind != "item" || rel.FromId != itemId) return OpResult.NotFound();

        session.Delete(rel);
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    /// <summary>Idempotent: references with no matching edge are ignored.</summary>
    public async Task<OpResult> UnlinkItemBatchAsync(
        Guid principalId, Guid itemId, DeleteRelationsBatchRequest r, CancellationToken ct = default)
    {
        if (r.ToRefs.Count > BatchMax) return OpResult.Invalid($"At most {BatchMax} references per batch.");
        var item = await session.LoadAsync<CalendarItem>(itemId, ct);
        if (item is null || item.DeletedAt is not null) return OpResult.NotFound();
        if (!await access.CanWriteItemAsync(principalId, item, ct)) return OpResult.Forbidden("No write access to this item.");

        session.DeleteWhere<Relation>(x => x.FromKind == "item" && x.FromId == itemId && x.ToKind == r.ToKind
            && x.RelationType == r.RelationType && r.ToRefs.Contains(x.ToRef));
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    public async Task<OpResult<List<RelationDto>>> ListForItemAsync(Guid principalId, Guid itemId, CancellationToken ct = default)
    {
        var item = await session.LoadAsync<CalendarItem>(itemId, ct);
        if (item is null || item.DeletedAt is not null) return OpResult<List<RelationDto>>.NotFound();
        if (!await access.CanReadItemAsync(principalId, item, ct)) return OpResult<List<RelationDto>>.Forbidden("No access to this item.");
        var rels = await session.Query<Relation>().Where(x => x.FromKind == "item" && x.FromId == itemId).ToListAsync(ct);
        return OpResult<List<RelationDto>>.Ok(rels.Select(RelationMapper.ToResponse).ToList());
    }

    /// <summary>Every edge of one kind the caller can see, as raw relations rather than items — the
    /// caller needs the ToRef→FromId mapping itself (e.g. to badge a photo grid), which the item-shaped
    /// reverse lookup cannot express without one call per reference.</summary>
    public async Task<OpResult<List<RelationDto>>> ListEdgesByKindAsync(Guid principalId, string toKind, CancellationToken ct = default)
    {
        var rels = await session.Query<Relation>().Where(x => x.FromKind == "item" && x.ToKind == toKind).ToListAsync(ct);
        if (rels.Count == 0) return OpResult<List<RelationDto>>.Ok([]);

        // A Relation carries no principal — visibility comes from the item it hangs off.
        var ids = rels.Select(r => r.FromId).Distinct().ToList();
        var items = await session.Query<CalendarItem>().Where(i => ids.Contains(i.Id) && i.DeletedAt == null).ToListAsync(ct);
        var calIds = await access.AccessibleCalendarIdsAsync(principalId, ct);
        var visibleIds = items
            .Where(i => i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && calIds.Contains(m.CalendarId)))
            .Select(i => i.Id)
            .ToHashSet();

        return OpResult<List<RelationDto>>.Ok(
            [.. rels.Where(r => visibleIds.Contains(r.FromId)).Select(RelationMapper.ToResponse)]);
    }

    /// <summary>Reverse lookup: items the caller can access that link to a given external reference.</summary>
    public async Task<OpResult<List<CalendarItemDto>>> FindItemsLinkedToAsync(Guid principalId, string toKind, string toRef, CancellationToken ct = default)
    {
        var rels = await session.Query<Relation>().Where(x => x.FromKind == "item" && x.ToKind == toKind && x.ToRef == toRef).ToListAsync(ct);
        var ids = rels.Select(r => r.FromId).Distinct().ToList();
        var items = await session.Query<CalendarItem>().Where(i => ids.Contains(i.Id) && i.DeletedAt == null).ToListAsync(ct);
        var calIds = await access.AccessibleCalendarIdsAsync(principalId, ct);
        var visible = items.Where(i => i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && calIds.Contains(m.CalendarId))).ToList();
        var scores = await completeness.ScoreItemsAsync(visible, ct);
        return OpResult<List<CalendarItemDto>>.Ok([.. visible.Select(i => i.ToResponse(scores[i.Id]))]);
    }
}
