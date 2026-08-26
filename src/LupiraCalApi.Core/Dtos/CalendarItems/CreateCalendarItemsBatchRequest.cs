namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Create many items in one call. Items may reference their parent by <c>ParentSourceKey</c> (the parent's
/// <c>SourceKey</c>) in any order — the server orders parents before children. Idempotent per item on <c>SourceKey</c>.</summary>
public sealed class CreateCalendarItemsBatchRequest
{
    public required List<CreateCalendarItemRequest> Items { get; set; }
}
