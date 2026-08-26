namespace LupiraCalApi.Core.Dtos.Sync;

/// <summary>One page of the changes feed. <c>Cursor</c> is opaque — hand it back as <c>?since=</c>; loop while
/// <c>HasMore</c>. A full sync (no <c>since</c>) streams every live visible item and suppresses tombstones —
/// the client replaces its mirror wholesale and rebases pending work.</summary>
public sealed class SyncChangesResponse
{
    public required string Cursor { get; set; }
    public required bool HasMore { get; set; }
    public required IReadOnlyList<SyncChangeDto> Changed { get; set; }

    /// <summary>Ids no longer visible to the caller: soft-deleted, or every accepted membership left the caller's
    /// readable calendars. Unknown ids are safe to ignore.</summary>
    public required IReadOnlyList<Guid> Deleted { get; set; }
}
