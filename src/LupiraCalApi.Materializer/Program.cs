using Lupira.Hosting.Observability;

namespace LupiraCalApi.Materializer;

// An explicit Program class (not top-level statements) so the global-namespace Program stays unique to the API
// host — the integration test project references the hosts.
public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // AddCalScheduling brings the Solo async daemon + horizon sweep. Exactly one replica of this container
        // may run: Solo claims exclusive ownership of the projection.
        builder.Services.AddCalCore().AddCalScheduling();

        builder.AddLupiraTelemetry("lupira-cal-materializer", o => o.Sources.Add("LupiraCalApi.Materializer"));

        var app = builder.Build();

        // Stack-local surface only — this container publishes no host port.
        app.MapGet("/livez", () => TypedResults.Ok())
            .DisableHttpMetrics();

        app.Run();
    }
}
