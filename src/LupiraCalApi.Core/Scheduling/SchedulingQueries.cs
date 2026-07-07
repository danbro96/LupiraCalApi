using LupiraCalApi.Domain;
using Marten;

namespace LupiraCalApi.Scheduling;

internal static class SchedulingQueries
{
    /// <summary>Resolves the item's fire calendar. Accepted memberships win over Proposed (a proposed system item must
    /// still fire); a payload system calendar (LlmPrompts/DevOps) wins over agenda ones. Null when the item is in no
    /// calendar — such a fire has no principal to deliver as, so it is not materialized.</summary>
    public static async Task<FireContext?> FireContextAsync(IQuerySession session, CalendarItem item, CancellationToken ct)
    {
        var memberships = item.Calendars
            .Where(m => m.Status != CalendarEntryStatus.Removed)
            .OrderBy(m => m.Status == CalendarEntryStatus.Accepted ? 0 : 1)
            .Select(m => m.CalendarId)
            .ToList();
        if (memberships.Count == 0) return null;

        var cals = await session.Query<Calendar>().Where(c => memberships.Contains(c.Id)).ToListAsync(ct);
        var byId = cals.ToDictionary(c => c.Id);
        var ordered = memberships.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        if (ordered.Count == 0) return null;

        var fireCal = ordered.FirstOrDefault(c => c.Kind is CalendarKind.LlmPrompts or CalendarKind.DevOps) ?? ordered[0];

        var principalId = (await session.Query<CalendarOwner>()
                .Where(o => o.CalendarId == fireCal.Id && o.Access == Access.Owner)
                .ToListAsync(ct))
            .OrderBy(o => o.PrincipalId)
            .FirstOrDefault()?.PrincipalId;

        return new FireContext(fireCal.Id, fireCal.Kind, principalId);
    }
}
