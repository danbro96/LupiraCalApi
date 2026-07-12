namespace LupiraCalApi.Dtos.CalendarItems;

/// <summary>Create many items in one call. Items may reference their parent by <c>ParentSourceKey</c> (the parent's
/// <c>SourceKey</c>) in any order — the server orders parents before children. Idempotent per item on <c>SourceKey</c>.</summary>
public sealed class CreateCalendarItemsBatchRequest
{
    public required List<CreateCalendarItemRequest> Items { get; set; }
}

/// <summary>Per-item outcome of a batch create, aligned index-for-index with the request. <c>Status</c> is
/// <c>created</c> | <c>existed</c> (idempotent hit on SourceKey) | <c>invalid</c> (see <c>Error</c>).</summary>
public sealed record ItemBatchResult(string? SourceKey, Guid? ItemId, string Status, string? Error);
