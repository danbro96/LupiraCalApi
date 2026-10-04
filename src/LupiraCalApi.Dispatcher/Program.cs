using Lupira.Clients.ServiceTokens;
using Lupira.Hosting.Observability;
using LupiraCalApi.Dispatcher.Clients;
using LupiraCalApi.Dispatcher.Dispatch;
using Npgsql;

namespace LupiraCalApi.Dispatcher;

// An explicit Program class (not top-level statements) so the global-namespace Program stays unique to the API
// host — the integration test project references both hosts.
public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<DispatcherOptions>(builder.Configuration.GetSection(DispatcherOptions.SectionName));
        builder.Services.Configure<AssistantOptions>(builder.Configuration.GetSection(AssistantOptions.SectionName));

        // Shared service graph and store config. No AddCalScheduling: materialization is the materializer's job,
        // and DaemonMode.Solo tolerates exactly one host — this one is free to scale on SKIP LOCKED claims.
        builder.Services.AddCalCore();

        // Raw Npgsql for the claim/transition SQL (the location/health-api split: Marten docs + a plain relational table).
        builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(
            sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
                ?? CoreServiceCollectionExtensions.DefaultConnectionString));

        builder.Services.AddHttpClient<AssistantFireClient>((sp, http) =>
        {
            var baseUrl = sp.GetRequiredService<IConfiguration>().GetSection(AssistantOptions.SectionName)["BaseUrl"];
            if (!string.IsNullOrWhiteSpace(baseUrl))
                http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            http.Timeout = TimeSpan.FromSeconds(30);
        }).AddLupiraServiceToken<AssistantOptions>();
        builder.Services.AddScoped<FireDispatchService>();

        var assistantConfigured = !string.IsNullOrWhiteSpace(builder.Configuration.GetSection(AssistantOptions.SectionName)["BaseUrl"]);
        if (assistantConfigured)
            builder.Services.AddHostedService<FireDispatchWorker>();

        builder.AddLupiraTelemetry("lupira-cal-dispatcher", o => o.Sources.Add("LupiraCalApi.Dispatcher"));

        var app = builder.Build();

        if (!assistantConfigured)
            app.Logger.LogWarning("Assistant:BaseUrl is not configured — the dispatcher is NOT running; scheduled fires will not be delivered.");

        // Stack-local surface only — this container publishes no host port.
        app.MapGet("/livez", () => TypedResults.Ok())
            .DisableHttpMetrics();

        app.Run();
    }
}
