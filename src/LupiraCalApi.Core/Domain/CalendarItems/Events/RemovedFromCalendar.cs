namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

public sealed record RemovedFromCalendar(Guid ItemId, Guid CalendarId, DateTimeOffset At, Guid? CommandId = null);
