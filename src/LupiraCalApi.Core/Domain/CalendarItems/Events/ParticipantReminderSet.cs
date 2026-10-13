namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

public sealed record ParticipantReminderSet(Guid ItemId, Guid ParticipationId, ParticipantReminder Reminder, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
