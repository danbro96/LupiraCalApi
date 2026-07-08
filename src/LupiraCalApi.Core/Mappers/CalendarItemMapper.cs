using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using System.Text.Json.Nodes;

namespace LupiraCalApi.Mappers;

/// <summary>Maps the <see cref="CalendarItem"/> snapshot to its response DTO. <paramref name="completeness"/> is computed
/// by the service (it needs the item's calendar kinds to decide exemption).</summary>
internal static class CalendarItemMapper
{
    public static CalendarItemDto ToResponse(this CalendarItem i, CompletenessScore? completeness) => new()
    {
        Id = i.Id,
        ExternalId = i.ExternalId,
        Title = i.Title,
        Description = i.Description,
        Status = i.Status,
        IsAllDay = i.IsAllDay,
        StartsAt = i.StartsAt,
        EndsAt = i.EndsAt,
        StartDate = i.StartDate,
        EndDate = i.EndDate,
        RecurrenceRule = i.RecurrenceRule,
        Kind = i.Kind,
        KindDetails = i.KindDetails,
        PlaceId = i.PlaceId,
        ParentItemId = i.ParentItemId,
        Tags = i.Tags,
        Metadata = JsonNode.Parse(string.IsNullOrWhiteSpace(i.Metadata) ? "{}" : i.Metadata),
        Prompt = i.Prompt,
        Action = i.Action,
        Completeness = completeness,
        Attendees = i.Attendees,
        Calendars = i.Calendars.Select(m => new CalendarMembershipDto { CalendarId = m.CalendarId, Status = m.Status }).ToList(),
        Etag = i.ContentHash,
    };
}
