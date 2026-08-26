namespace LupiraCalApi.Core.Dtos.Sync;

/// <summary>One section's last-writer guard: the (occurredAt, commandId) of the write that owns its current
/// value. Offline clients seed their local guards from these so a pending edit on one section never blocks —
/// and is never clobbered by — fresher server state on another.</summary>
public sealed class SectionGuardDto
{
    public required DateTimeOffset Ts { get; set; }
    public required Guid Cmd { get; set; }

    internal static SectionGuardDto From(DateTimeOffset ts, Guid cmd) => new() { Ts = ts, Cmd = cmd };
}
