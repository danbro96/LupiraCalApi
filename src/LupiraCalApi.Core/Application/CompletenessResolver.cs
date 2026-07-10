using LupiraCalApi.Domain;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>Resolves the derived completeness score for items. It lives outside the snapshot because
/// item exemption needs the item's calendar kinds — not visible to a single-stream snapshot.</summary>
public sealed class CompletenessResolver(IQuerySession session)
{
    public async Task<CompletenessScore?> ScoreItemAsync(CalendarItem item, CancellationToken ct = default)
    {
        var exempt = await ExemptCalendarIdsAsync([.. AcceptedIds(item)], ct);
        return CompletenessScorer.ScoreItem(item, AcceptedIds(item).Any(exempt.Contains));
    }

    public async Task<Dictionary<Guid, CompletenessScore?>> ScoreItemsAsync(IReadOnlyCollection<CalendarItem> items, CancellationToken ct = default)
    {
        var exempt = await ExemptCalendarIdsAsync([.. items.SelectMany(AcceptedIds).Distinct()], ct);
        return items.ToDictionary(i => i.Id, i => CompletenessScorer.ScoreItem(i, AcceptedIds(i).Any(exempt.Contains)));
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
