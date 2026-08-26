using System.Text.Json.Nodes;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Completeness;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;

namespace LupiraCalApi.Core.Mappers;

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
        StartPrecision = i.StartPrecision,
        EndPrecision = i.EndPrecision,
        RecurrenceRule = i.RecurrenceRule,
        Category = i.Category,
        Details = i.Details,
        PlaceId = i.PlaceId,
        LocationLabel = i.LocationLabel,
        ParentItemId = i.ParentItemId,
        Tags = i.Tags,
        Metadata = JsonNode.Parse(string.IsNullOrWhiteSpace(i.Metadata) ? "{}" : i.Metadata),
        Prompt = i.Prompt,
        Action = i.Action,
        Completeness = completeness,
        Attendees = [.. i.Attendees.Select(ToResponse)],
        Calendars = i.Calendars.Select(m => new CalendarMembershipDto { CalendarId = m.CalendarId, Status = m.Status }).ToList(),
        Etag = i.ContentHash,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt,
        Version = i.Version,
    };

    private static ItemAttendeeDto ToResponse(ItemAttendee a) => new()
    {
        ParticipationId = a.ParticipationId,
        ContactId = a.ContactId,
        Role = a.Role,
        Status = a.Status,
        InvitedAt = a.InvitedAt,
        RespondedAt = a.RespondedAt,
        AttendedAt = a.AttendedAt,
        LeftAt = a.LeftAt,
    };
}
