using System.Text.Json.Serialization;
using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Dtos.CalendarItems;

public sealed class CalendarMembershipDto
{
    public required Guid CalendarId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<CalendarEntryStatus>))]
    public required CalendarEntryStatus Status { get; set; }
}
