using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>A participant's own reminder for an item; <c>MinutesBefore</c> is set only for <c>Before</c>.</summary>
public sealed class ParticipantReminderDto
{
    public required ReminderMode Mode { get; set; }

    public required int? MinutesBefore { get; set; }
}
