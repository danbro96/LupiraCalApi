using LupiraCalApi.Core.Dtos.Calendars;

namespace LupiraCalApi.Core.Dtos.Sync;

/// <summary>Snapshot of the caller's containers. Containers are plain documents with no event history, so they
/// have no cursor — fetch once per sync cycle and diff against the mirror.</summary>
public sealed class SyncContainersResponse
{
    public required IReadOnlyList<ContainerDto> Calendars { get; set; }
}
