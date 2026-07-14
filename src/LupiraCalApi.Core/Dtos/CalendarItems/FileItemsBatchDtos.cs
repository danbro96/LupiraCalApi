namespace LupiraCalApi.Dtos.CalendarItems;

/// <summary>One entry in a batch file operation: file existing item <c>ItemId</c> into calendar <c>CalendarId</c>.
/// <c>Status</c> = proposed | accepted (default proposed).</summary>
public sealed class FileItemRequest
{
    public required Guid ItemId { get; set; }
    public required Guid CalendarId { get; set; }
    public string? Status { get; set; }
}

/// <summary>File many existing items into calendars in one call. Each entry is authorized independently.</summary>
public sealed class FileItemsBatchRequest
{
    public required List<FileItemRequest> Entries { get; set; }
}

/// <summary>Per-entry outcome of a batch file, in input order. <c>Status</c> is <c>filed</c> | <c>notfound</c>
/// (missing or inaccessible item — opaque) | <c>forbidden</c> (no write access to the calendar) | <c>invalid</c>.</summary>
public sealed record FileItemResult(Guid ItemId, Guid CalendarId, string Status, string? Error);
