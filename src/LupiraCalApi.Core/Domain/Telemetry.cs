using System.Diagnostics;

namespace LupiraCalApi.Domain;

/// <summary>Domain-specific tracing source, registered with OpenTelemetry in Program.cs.</summary>
public static class Telemetry
{
    public const string ActivitySourceName = "LupiraCalApi.Calendar";
    public static readonly ActivitySource Source = new(ActivitySourceName);
}
