namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

public sealed record AttendanceConfirmed(Guid ItemId, Guid ParticipationId, DateTimeOffset At);
