using System.Net;
using Lupira.Clients.ServiceTokens;
using Lupira.Hosting.Observability;
using LupiraCalApi.Core.Application.Subscriptions;
using LupiraCalApi.Dispatcher.Clients;
using LupiraCalApi.Dispatcher.Dispatch;
using LupiraCalApi.Dispatcher.Subscriptions;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Instrumentation.Http;

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

        // Feed refresh runs whether or not fire delivery is configured. Redirects stay manual so each hop is re-checked.
        builder.Services.AddHttpClient<SubscriptionFeedClient>((sp, http) =>
        {
            http.Timeout = sp.GetRequiredService<IOptions<SubscriptionOptions>>().Value.Timeout;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LupiraCal");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = PrivateAddressGuard.ConnectAsync,
            AutomaticDecompression = DecompressionMethods.All,
        });
        builder.Services.AddScoped<SubscriptionRefreshService>();
        builder.Services.AddHostedService<SubscriptionRefreshWorker>();
        // A private feed address works like a password: its spans keep only the host.
        builder.Services.Configure<HttpClientTraceInstrumentationOptions>(o => o.EnrichWithHttpRequestMessage = (activity, request) =>
        {
            if (request.Options.TryGetValue(SubscriptionFeedClient.FeedRequest, out _) && request.RequestUri is { } uri)
                activity.SetTag("url.full", $"{uri.Scheme}://{uri.IdnHost}/");
        });

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
