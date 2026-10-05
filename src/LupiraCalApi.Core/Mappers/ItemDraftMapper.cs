using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Serialization;

namespace LupiraCalApi.Core.Mappers;

/// <summary>Maps a parsed calendar-file event to an unsaved item draft.</summary>
internal static class ItemDraftMapper
{
    public static ItemDraftDto ToDraft(this ParsedEvent p, string sourceKey) => new()
    {
        SourceKey = sourceKey,
        Title = p.Title,
        Description = p.Description,
        Status = p.Status,
        Category = p.Category,
        IsAllDay = p.IsAllDay,
        StartsAt = p.StartsAt,
        EndsAt = p.EndsAt,
        StartDate = p.StartDate,
        EndDate = p.EndDate,
        StartTimezone = p.IsAllDay ? null : TimeZoneIds.Find(p.StartTimezone)?.Id,
        RecurrenceRule = p.RecurrenceRule,
        Location = string.IsNullOrWhiteSpace(p.Location) ? null : p.Location.Trim(),
    };
}
