namespace LupiraCalApi.Core.Domain.Holidays;

/// <summary>One holiday on one date. <c>Key</c> names the holiday across years.</summary>
public sealed record Holiday(string Key, string Name, DateOnly Date, HolidayKind Kind);
