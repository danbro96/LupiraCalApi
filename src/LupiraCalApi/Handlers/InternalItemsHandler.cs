using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Internal;
using Marten;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

/// <summary>How many calendar items reference each requested geo place id (item location + travel legs) — geo's
/// orphan sweep asks this before pruning. Deliberately ACL-free (the fence is the <c>internal:read</c> service scope
/// and the LAN-only edge); soft-deleted items are counted separately, never dropped, so geo can flag places that
/// only deleted items still reference. Zero-count ids are omitted.</summary>
public sealed class InternalItemsHandler(IQuerySession session)
{
    private const int MaxPlaceIds = 1000;

    public async Task<Results<Ok<ItemPlaceReferencesResponse>, BadRequest<string>>> CheckPlaceReferencesAsync(
        CheckPlaceReferencesRequest body, CancellationToken ct)
    {
        if (body.PlaceIds.Count == 0 || body.PlaceIds.Count > MaxPlaceIds)
            return TypedResults.BadRequest($"Between 1 and {MaxPlaceIds} ids per request.");
        var requested = body.PlaceIds.ToHashSet();

        var counts = new Dictionary<Guid, (int Live, int Deleted)>();
        var all = await session.Query<CalendarItem>().ToListAsync(ct);
        foreach (var item in all)
        {
            var refs = new[] { item.PlaceId, item.Details?.Travel?.ToPlaceId, item.Details?.Travel?.FromPlaceId };
            foreach (var pid in refs.OfType<Guid>().Distinct().Where(requested.Contains))
            {
                var c = counts.GetValueOrDefault(pid);
                counts[pid] = item.DeletedAt is null ? (c.Live + 1, c.Deleted) : (c.Live, c.Deleted + 1);
            }
        }

        return TypedResults.Ok(new ItemPlaceReferencesResponse
        {
            Places = [.. counts.Select(kv => new ItemPlaceRefDto { PlaceId = kv.Key, LiveCount = kv.Value.Live, DeletedCount = kv.Value.Deleted })],
        });
    }
}
