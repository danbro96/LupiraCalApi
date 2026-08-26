namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Per-entry outcome of a batch file, in input order. <c>Status</c> is <c>filed</c> | <c>notfound</c>
/// (missing or inaccessible item — opaque) | <c>forbidden</c> (no write access to the calendar) | <c>invalid</c>.</summary>
public sealed record FileItemResult(Guid ItemId, Guid CalendarId, string Status, string? Error);
