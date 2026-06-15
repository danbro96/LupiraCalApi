using System.Text.Json.Nodes;
using LupiraCalApi.Data.Entities;
using LupiraCalApi.Dtos.Events;

namespace LupiraCalApi.Mappers;

/// <summary>Maps the <see cref="Event"/> entity to its response DTO.</summary>
internal static class EventMapper
{
    public static EventDto ToResponse(this Event e) => new(
        e.Id, e.CalendarId, e.IcalUid, e.Title, e.Description, e.Location, e.Status, e.IsAllDay,
        e.StartsAt, e.EndsAt, e.StartDate, e.EndDate, e.RecurrenceRule, e.Tags,
        JsonNode.Parse(string.IsNullOrWhiteSpace(e.Metadata) ? "{}" : e.Metadata), e.ContentHash);
}
