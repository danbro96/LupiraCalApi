using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Application.Places;

/// <summary>The slice of a <see cref="CalendarItem"/> place usage reads: its place references, category, attendees and schedule.</summary>
public sealed class PlaceUsageRow
{
    public ItemStatus? Status { get; set; }

    public ItemCategory? Category { get; set; }

    public Guid? PlaceId { get; set; }

    public string? LocationLabel { get; set; }

    public Guid? FromPlaceId { get; set; }

    public string? FromLabel { get; set; }

    public Guid? ToPlaceId { get; set; }

    public string? ToLabel { get; set; }

    public bool IsAllDay { get; set; }

    public DateTimeOffset? StartsAt { get; set; }

    public DateOnly? StartDate { get; set; }

    public string? StartTimezone { get; set; }

    public string? RecurrenceRule { get; set; }

    public DateTimeOffset[]? ExcludedOccurrences { get; set; }

    public DateTimeOffset[]? ExtraOccurrences { get; set; }

    public OccurrenceOverride[]? OccurrenceOverrides { get; set; }

    public List<ItemAttendee>? Attendees { get; set; }

    internal CalendarItem Schedule() => new()
    {
        IsAllDay = IsAllDay,
        StartsAt = StartsAt,
        StartDate = StartDate,
        StartTimezone = StartTimezone,
        RecurrenceRule = RecurrenceRule,
        ExcludedOccurrences = ExcludedOccurrences,
        ExtraOccurrences = ExtraOccurrences,
        OccurrenceOverrides = OccurrenceOverrides,
    };
}
