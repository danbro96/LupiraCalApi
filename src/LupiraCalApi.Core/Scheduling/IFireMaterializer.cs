using LupiraCalApi.Core.Domain.CalendarItems;

namespace LupiraCalApi.Core.Scheduling;

public interface IFireMaterializer
{
    /// <summary>Expand an item's fired payload + recurrence into <see cref="ScheduledFireRow"/>s over [now, now+horizon].
    /// Empty when the item carries no payload, the payload is disabled, or it has no fire calendar (null context).</summary>
    IReadOnlyList<ScheduledFireRow> Materialize(CalendarItem item, FireContext? context, DateTimeOffset now, TimeSpan horizon);
}
