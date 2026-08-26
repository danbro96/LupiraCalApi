using LupiraCalApi.Core.Dtos.CalendarItems;

namespace LupiraCalApi.Core.Dtos.Sync;

/// <summary>A changed item: the full DTO plus its section guards.</summary>
public sealed class SyncChangeDto
{
    public required CalendarItemDto Item { get; set; }
    public required SectionGuardsDto Guards { get; set; }
}
