using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>An item's membership of a calendar (the <c>CalendarEntry</c> read model, embedded). <c>Removed</c> is kept as a sync tombstone.</summary>
public sealed class CalendarMembership
{
    public Guid CalendarId { get; set; }

    public CalendarEntryStatus Status { get; set; }
}
