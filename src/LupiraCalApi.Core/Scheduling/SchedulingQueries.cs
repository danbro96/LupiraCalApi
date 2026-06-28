using LupiraCalApi.Domain;
using Marten;

namespace LupiraCalApi.Scheduling;

internal static class SchedulingQueries
{
    /// <summary>The calendar kind that drives <c>expire_after</c> — the system calendar the payload lives in (LlmPrompts/DevOps),
    /// or null (→ 24h fallback) for anything else.</summary>
    public static async Task<CalendarKind?> ExpireKindAsync(IQuerySession session, CalendarItem item, CancellationToken ct)
    {
        var ids = item.Calendars.Where(m => m.Status == CalendarEntryStatus.Accepted).Select(m => m.CalendarId).ToList();
        if (ids.Count == 0) return null;
        var cals = await session.Query<Calendar>().Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        return cals.FirstOrDefault(c => c.Kind is CalendarKind.LlmPrompts or CalendarKind.DevOps)?.Kind;
    }
}
