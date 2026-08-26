using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

public sealed record AttendeeInvited(Guid ItemId, Guid ParticipationId, Guid ContactId, ParticipationRole Role, DateTimeOffset At);
