using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Calendars;

namespace LupiraCalApi.Dtos.Sync;

/// <summary>One section's last-writer guard: the (occurredAt, commandId) of the write that owns its current
/// value. Offline clients seed their local guards from these so a pending edit on one section never blocks —
/// and is never clobbered by — fresher server state on another.</summary>
public sealed class SectionGuardDto
{
    public required DateTimeOffset Ts { get; set; }
    public required Guid Cmd { get; set; }

    internal static SectionGuardDto From(DateTimeOffset ts, Guid cmd) => new() { Ts = ts, Cmd = cmd };
}

/// <summary>Per-section guards for an item: core fields, metadata, the XOR payload, and per-calendar filing.</summary>
public sealed class SectionGuardsDto
{
    public required SectionGuardDto Core { get; set; }
    public required SectionGuardDto Metadata { get; set; }
    public required SectionGuardDto Payload { get; set; }
    public required Dictionary<Guid, SectionGuardDto> Filing { get; set; }

    internal static SectionGuardsDto From(CalendarItem i) => new()
    {
        Core = SectionGuardDto.From(i.CoreTs, i.CoreCmd),
        Metadata = SectionGuardDto.From(i.MetadataTs, i.MetadataCmd),
        Payload = SectionGuardDto.From(i.PayloadTs, i.PayloadCmd),
        Filing = i.FilingTs.ToDictionary(
            kv => kv.Key,
            kv => SectionGuardDto.From(kv.Value, i.FilingCmd.GetValueOrDefault(kv.Key))),
    };
}

/// <summary>A changed item: the full DTO plus its section guards.</summary>
public sealed class SyncChangeDto
{
    public required CalendarItemDto Item { get; set; }
    public required SectionGuardsDto Guards { get; set; }
}

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

/// <summary>Snapshot of the caller's containers. Containers are plain documents with no event history, so they
/// have no cursor — fetch once per sync cycle and diff against the mirror.</summary>
public sealed class SyncContainersResponse
{
    public required IReadOnlyList<ContainerDto> Calendars { get; set; }
}
