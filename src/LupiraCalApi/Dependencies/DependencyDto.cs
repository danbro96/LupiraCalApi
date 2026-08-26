namespace LupiraCalApi.Dependencies;

public sealed class DependencyDto
{
    public required string Name { get; set; }

    public required DependencyStatus Status { get; set; }

    public double? LatencyMs { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset? CheckedUtc { get; set; }
}
