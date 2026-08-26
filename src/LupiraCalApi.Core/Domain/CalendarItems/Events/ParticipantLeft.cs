namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

public sealed record ParticipantLeft(Guid ItemId, Guid ParticipationId, DateTimeOffset At);
