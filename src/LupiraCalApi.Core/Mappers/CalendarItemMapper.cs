using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using System.Text.Json.Nodes;

namespace LupiraCalApi.Mappers;

/// <summary>Maps the <see cref="CalendarItem"/> snapshot to its response DTO.</summary>
internal static class CalendarItemMapper
{
    public static CalendarItemDto ToResponse(this CalendarItem i) => new(
        i.Id, i.IcalUid, i.Title, i.Description, i.Status?.ToString(), i.IsAllDay,
        i.StartsAt, i.EndsAt, i.StartDate, i.EndDate, i.RecurrenceRule, i.Kind?.ToString(),
        i.PlaceId, i.ParentItemId, i.Tags,
        JsonNode.Parse(string.IsNullOrWhiteSpace(i.Metadata) ? "{}" : i.Metadata),
        i.Calendars.Select(m => new CalendarMembershipDto(m.CalendarId, m.Status.ToString())).ToList(),
        i.ContentHash);
}
