namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

public sealed record AttendeeRemoved(Guid ItemId, Guid ParticipationId);
