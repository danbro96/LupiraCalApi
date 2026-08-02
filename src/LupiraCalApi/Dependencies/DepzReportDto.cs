namespace LupiraCalApi.Dependencies;

/// <summary>The /depz body; names are OTel service names — the registry join keys.</summary>
public sealed class DepzReportDto
{
    public required string Service { get; set; }
    public DateTimeOffset? LastPolledUtc { get; set; }
    public required IReadOnlyList<DependencyDto> Dependencies { get; set; }
}

public sealed class DependencyDto
{
    public required string Name { get; set; }
    public required DependencyStatus Status { get; set; }
    public double? LatencyMs { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? CheckedUtc { get; set; }
}
