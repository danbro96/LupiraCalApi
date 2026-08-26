using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

public sealed record AddedToCalendar(Guid ItemId, Guid CalendarId, CalendarEntryStatus Status, DateTimeOffset At, Guid? CommandId = null);
