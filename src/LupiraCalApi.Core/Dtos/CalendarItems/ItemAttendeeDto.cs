using System.Text.Json.Serialization;
using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>One attendee's participation, composed from the participation events.</summary>
public sealed class ItemAttendeeDto
{
    public required Guid ParticipationId { get; set; }

    public required Guid ContactId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ParticipationRole>))]
    public required ParticipationRole Role { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ParticipationStatus>))]
    public required ParticipationStatus Status { get; set; }

    public required DateTimeOffset? InvitedAt { get; set; }

    public required DateTimeOffset? RespondedAt { get; set; }

    public required DateTimeOffset? AttendedAt { get; set; }

    public required DateTimeOffset? LeftAt { get; set; }
}
