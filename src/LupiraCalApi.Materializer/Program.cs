using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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

        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(r => r.AddService(
                    serviceName: "lupira-cal-materializer",
                    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"))
                .WithTracing(t => t
                    .AddSource("LupiraCalApi.Materializer")
                    .AddAspNetCoreInstrumentation(o => o.RecordException = true)
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter())
                .WithMetrics(m => m
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddOtlpExporter());

            builder.Logging.AddOpenTelemetry(o =>
            {
                o.IncludeFormattedMessage = true;
                o.IncludeScopes = true;
                o.AddOtlpExporter();
            });
        }

        var app = builder.Build();

        // Stack-local surface only — this container publishes no host port.
        app.MapGet("/livez", () => TypedResults.Ok())
            .DisableHttpMetrics();

        app.Run();
    }
}
