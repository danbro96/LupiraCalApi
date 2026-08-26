using LupiraCalApi.Core.Domain.CalendarItems;

namespace LupiraCalApi.Core.Dtos.Sync;

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
