namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Per-entry outcome of a batch file, in input order. <c>Status</c> is <c>Filed</c> | <c>NotFound</c>
/// (missing or inaccessible item — opaque) | <c>Forbidden</c> (no write access to the calendar) | <c>Conflict</c> |
/// <c>Unavailable</c> (retry the entry) | <c>Invalid</c>.</summary>
public sealed class FileItemResult
{
    public required Guid ItemId { get; set; }

    public required Guid CalendarId { get; set; }

    public required FileItemStatus Status { get; set; }

    public required string? Error { get; set; }
}
