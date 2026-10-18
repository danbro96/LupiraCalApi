namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Per-entry outcome of a batch file, in input order. <c>Status</c> is <c>filed</c> | <c>notfound</c>
/// (missing or inaccessible item — opaque) | <c>forbidden</c> (no write access to the calendar) | <c>invalid</c>.</summary>
public sealed class FileItemResult
{
    public required Guid ItemId { get; set; }

    public required Guid CalendarId { get; set; }

    public required string Status { get; set; }

    public required string? Error { get; set; }
}
