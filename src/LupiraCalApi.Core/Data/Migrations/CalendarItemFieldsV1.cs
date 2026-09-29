using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Data.Migrations;

/// <summary>The <see cref="CalendarItemFields"/> shape stored before per-occurrence deviations were structured (they were raw text).</summary>
public sealed record CalendarItemFieldsV1(
    string? Title,
    string? Description,
    ItemStatus? Status,
    bool IsAllDay,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? StartTimezone,
    string? EndTimezone,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? RecurrenceRule,
    string? RecurrenceExceptions,
    string? RecurrenceOverrides,
    ItemCategory? Category,
    Guid? PlaceId,
    string? LocationLabel,
    Guid? ParentItemId,
    string[]? Tags,
    DatePrecision? StartPrecision = null,
    DatePrecision? EndPrecision = null);
