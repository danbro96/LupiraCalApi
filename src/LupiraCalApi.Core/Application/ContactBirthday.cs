namespace LupiraCalApi.Core.Application;

/// <summary>A contact's birthday, feeding the read-time Birthdays projection. <c>Year</c> is null when only the
/// month-day is known — the birthday still recurs yearly, just without an age.</summary>
public sealed record ContactBirthday(Guid ContactId, string DisplayName, int? Year, int Month, int Day);
