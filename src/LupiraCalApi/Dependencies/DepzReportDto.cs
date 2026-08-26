namespace LupiraCalApi.Dependencies;

/// <summary>The /depz body; names are OTel service names — the registry join keys.</summary>
public sealed class DepzReportDto
{
    public required string Service { get; set; }

    public DateTimeOffset? LastPolledUtc { get; set; }

    public required IReadOnlyList<DependencyDto> Dependencies { get; set; }
}
