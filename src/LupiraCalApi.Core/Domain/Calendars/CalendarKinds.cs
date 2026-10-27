using Lupira.Contracts.Fires;

namespace LupiraCalApi.Core.Domain.Calendars;

public static class CalendarKinds
{
    /// <summary>Content computed at read time; a stored item filed there would shadow it, so they take no writes.</summary>
    public static bool IsProjected(CalendarKind kind) => kind is CalendarKind.Birthdays or CalendarKind.Holidays;
}
