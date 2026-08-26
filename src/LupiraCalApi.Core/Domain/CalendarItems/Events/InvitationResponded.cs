using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

public sealed record InvitationResponded(Guid ItemId, Guid ParticipationId, ParticipationStatus Status, DateTimeOffset At);
