namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>One entry in a batch file operation: file existing item <c>ItemId</c> into calendar <c>CalendarId</c>.
/// <c>Status</c> = proposed | accepted (default proposed).</summary>
public sealed class FileItemRequest
{
    public required Guid ItemId { get; set; }
    public required Guid CalendarId { get; set; }
    public string? Status { get; set; }
}
