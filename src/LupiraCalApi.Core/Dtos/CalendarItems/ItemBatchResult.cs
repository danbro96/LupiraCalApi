namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Per-item outcome of a batch create, aligned index-for-index with the request. <c>Status</c> is
/// <c>Created</c> | <c>Existed</c> (idempotent hit on SourceKey) | <c>Forbidden</c> (no access to the calendar, or the SourceKey
/// belongs to an item the caller can't access; no <c>ItemId</c>) | <c>Invalid</c> (see <c>Error</c>) | <c>Unavailable</c>
/// (a place couldn't be verified with geo; retry the item).</summary>
public sealed class ItemBatchResult
{
    public required string? SourceKey { get; set; }

    public required Guid? ItemId { get; set; }

    public required ItemBatchStatus Status { get; set; }

    public required string? Error { get; set; }
}
