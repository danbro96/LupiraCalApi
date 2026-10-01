namespace LupiraCalApi.Core.Dtos.Sync;

/// <summary>One page of the changes feed. <c>Cursor</c> is opaque — hand it back as <c>?since=</c>; loop while
/// <c>HasMore</c>. A full sync (no <c>since</c>, or a <c>Reset</c>) streams every live visible item and suppresses
/// tombstones — the client replaces its mirror wholesale and rebases pending work.</summary>
public sealed class SyncChangesResponse
{
    public required string Cursor { get; set; }

    public required bool HasMore { get; set; }

    /// <summary>Stream restarted from zero (no <c>since</c>, or readable calendars changed): a full sync from here.</summary>
    public required bool Reset { get; set; }

    public required IReadOnlyList<SyncChangeDto> Changed { get; set; }

    /// <summary>Ids filed to a readable calendar but no longer visible (deleted, or no accepted membership). Unknown
    /// ids are safe to ignore.</summary>
    public required IReadOnlyList<Guid> Deleted { get; set; }
}
