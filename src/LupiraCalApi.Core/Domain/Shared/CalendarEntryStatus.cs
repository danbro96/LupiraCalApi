using LupiraCalApi.Core.Domain.CalendarItems;

namespace LupiraCalApi.Core.Domain.Shared;

/// <summary>Curation state of a <see cref="CalendarItem"/> within a calendar. <c>Removed</c> is retained as a sync tombstone.</summary>
public enum CalendarEntryStatus
{
    Proposed,
    Accepted,
    Removed,
}
