namespace LupiraCalApi.Dependencies;

/// <summary>Last completed sweep, atomically swapped so /depz serves from memory.</summary>
public sealed class DependencyReportCache
{
    public const string ServiceName = "lupira-cal-api";

    private volatile DepzReportDto _report = new()
    {
        Service = ServiceName,
        LastPolledUtc = null,
        Dependencies = [],
    };

    public DepzReportDto Current() => _report;

    public void Set(DepzReportDto report) => _report = report;
}
