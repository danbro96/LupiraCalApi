namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Per-item outcome of a batch create, aligned index-for-index with the request. <c>Status</c> is
/// <c>created</c> | <c>existed</c> (idempotent hit on SourceKey) | <c>forbidden</c> (no access to the calendar, or the SourceKey
/// belongs to an item the caller can't access; no <c>ItemId</c>) | <c>invalid</c> (see <c>Error</c>).</summary>
public sealed record ItemBatchResult(string? SourceKey, Guid? ItemId, string Status, string? Error);
