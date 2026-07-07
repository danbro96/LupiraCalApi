using JasperFx;
using LupiraCalApi.Domain;
using LupiraCalApi.Worker.Clients;
using LupiraCalApi.Worker.Dispatch;
using Marten;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LupiraCalApi.Worker;

// An explicit Program class (not top-level statements) so the global-namespace Program stays unique to the API
// host — the integration test project references both hosts.
public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<DispatcherOptions>(builder.Configuration.GetSection(DispatcherOptions.SectionName));
        builder.Services.Configure<AssistantOptions>(builder.Configuration.GetSection(AssistantOptions.SectionName));

        // Same store shape as cal-api (read side only): no async daemon, no projections, no schema ownership —
        // cal-api owns the Marten schema; the worker's startup only ensures the raw scheduled_fire table.
        builder.Services.AddMarten(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
                ?? CoreServiceCollectionExtensions.DefaultConnectionString;
            var opts = new StoreOptions();
            opts.Connection(connectionString);
            opts.UseLupiraCal();
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            return opts;
        }).UseLightweightSessions();

        // Raw Npgsql for the claim/transition SQL (the location/health-api split: Marten docs + a plain relational table).
        builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(
            sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
                ?? CoreServiceCollectionExtensions.DefaultConnectionString));

        builder.Services.AddSingleton<ServiceTokenProvider>();
        builder.Services.AddHttpClient(nameof(ServiceTokenProvider), http => http.Timeout = TimeSpan.FromSeconds(15));
        builder.Services.AddHttpClient<AssistantFireClient>((sp, http) =>
        {
            var baseUrl = sp.GetRequiredService<IConfiguration>().GetSection(AssistantOptions.SectionName)["BaseUrl"];
            if (!string.IsNullOrWhiteSpace(baseUrl))
                http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            http.Timeout = TimeSpan.FromSeconds(30);
        });
        builder.Services.AddScoped<FireDispatchService>();

        var assistantConfigured = !string.IsNullOrWhiteSpace(builder.Configuration.GetSection(AssistantOptions.SectionName)["BaseUrl"]);
        if (assistantConfigured)
            builder.Services.AddHostedService<FireDispatchWorker>();

        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(r => r.AddService(
                    serviceName: "lupira-cal-worker",
                    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"))
                .WithTracing(t => t
                    .AddSource("LupiraCalApi.Worker")
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

        if (!assistantConfigured)
            app.Logger.LogWarning("Assistant:BaseUrl is not configured — the dispatcher is NOT running; scheduled fires will not be delivered.");

        // Stack-local surface only — this container publishes no host port.
        app.MapGet("/livez", () => TypedResults.Ok())
            .DisableHttpMetrics();

        app.Run();
    }
}
