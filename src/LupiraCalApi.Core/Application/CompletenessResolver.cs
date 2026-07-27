using LupiraCalApi.Domain;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>Resolves the derived completeness score for items. It lives outside the snapshot because
/// item exemption needs the item's calendar kinds — not visible to a single-stream snapshot.</summary>
public sealed class CompletenessResolver(IQuerySession session)
{
    public async Task<CompletenessScore?> ScoreItemAsync(CalendarItem item, CancellationToken ct = default) =>
        (await ScoreItemsAsync([item], ct))[item.Id];

    public async Task<Dictionary<Guid, CompletenessScore?>> ScoreItemsAsync(IReadOnlyCollection<CalendarItem> items, CancellationToken ct = default)
    {
        var exempt = await ExemptCalendarIdsAsync([.. items.SelectMany(AcceptedIds).Distinct()], ct);
        var parents = await ParentIdsWithChildrenAsync(items, ct);
        return items.ToDictionary(i => i.Id, i => CompletenessScorer.ScoreItem(i, AcceptedIds(i).Any(exempt.Contains), parents.Contains(i.Id)));
    }



    // A parent Trip's legs are child items; the scorer swaps its rubric for such parents, so resolve which
    // batch items have live children. Only queried when a Trip is present — other categories don't use the flag.
    private async Task<HashSet<Guid>> ParentIdsWithChildrenAsync(IReadOnlyCollection<CalendarItem> items, CancellationToken ct)
    {
        if (!items.Any(i => i.Category == ItemCategory.Trip)) return [];
        var parentIds = await session.Query<CalendarItem>()
            .Where(c => c.DeletedAt == null && c.ParentItemId != null)
            .Select(c => c.ParentItemId!.Value)
            .ToListAsync(ct);
        return [.. parentIds];
    }

    private static IEnumerable<Guid> AcceptedIds(CalendarItem i) =>
        i.Calendars.Where(m => m.Status == CalendarEntryStatus.Accepted).Select(m => m.CalendarId);

    // System calendars + the two special agenda kinds (Birthdays/Availability) are never check-in targets.
    private async Task<HashSet<Guid>> ExemptCalendarIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var cals = await session.Query<Calendar>().Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        return [.. cals.Where(c => c.Class == CalendarClass.System || c.Kind is CalendarKind.Birthdays or CalendarKind.Availability).Select(c => c.Id)];
    }

}
