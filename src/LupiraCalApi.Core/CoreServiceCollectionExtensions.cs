using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx;
using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Data;
using LupiraCalApi.Domain;
using LupiraCalApi.Scheduling;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the LupiraCalApi bounded context (Marten event store + document store + transport-neutral services) into the host's DI container.</summary>
public static class CoreServiceCollectionExtensions
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=lupira_cal;Username=lupira_cal_user;Password=devpassword";

    /// <summary>
    /// The service graph every cal host shares. Registers no hosted services and opens no connection, so a host
    /// that only serves requests never touches Postgres at startup. Returns the Marten builder so a background
    /// host can chain <see cref="AddCalScheduling"/> — <c>AddAsyncDaemon</c> and <c>AddProjectionWithServices</c>
    /// exist only on that builder.
    /// </summary>
    public static MartenServiceCollectionExtensions.MartenConfigurationExpression AddCalCore(this IServiceCollection services)
    {
        // Resolve the connection string lazily from IConfiguration so test hosts (WebApplicationFactory) can
        // override ConnectionStrings:Postgres before the store is built.
        var marten = services.AddMarten(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres") ?? DefaultConnectionString;
            var opts = new StoreOptions();
            opts.Connection(connectionString);
            opts.UseLupiraCal();
            // Prod owns its DDL via `--apply-schema` (UseLupiraCal sets AutoCreate.None); dev/tests self-provision.
            if (sp.GetRequiredService<IHostEnvironment>().IsDevelopment())
                opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            return opts;
        }).UseLightweightSessions()
          // Declared in every host so all stores share one configuration — `--apply-schema` and `--rebuild-items`
          // run from the API container and must see it. Declaring is not running: with no daemon it stays inert.
          .AddProjectionWithServices<ScheduledFireProjection>(ProjectionLifecycle.Async, ServiceLifetime.Singleton, "scheduled_fire");

        services.AddSingleton<IFireMaterializer, FireMaterializer>();
        services.AddSingleton<RecurrenceExpander>();
        services.AddScoped<CompletenessResolver>();
        services.AddScoped<AccessResolver>();
        services.AddScoped<PrincipalDirectory>();
        // Default: no external gazetteer → free-text locations resolve to no id (label = raw text). The host overrides
        // this with an HTTP GeoApiClient when LupiraGeoApi is configured (Geo:BaseUrl).
        services.TryAddSingleton<IGeoResolver, NullGeoResolver>();
        // Same pattern for contacts: LupiraContactApi owns them; unconfigured -> fail-open null resolver.
        services.TryAddSingleton<IContactResolver, NullContactResolver>();
        services.AddScoped<LupiraCalApi.Data.Idempotency>();
        services.AddScoped<CalendarService>();
        services.AddScoped<CalendarItemService>();
        services.AddScoped<CurationService>();
        services.AddScoped<ParticipationService>();
        services.AddScoped<RelationService>();
        services.AddSingleton<TimeRangeFilter>();
        services.AddScoped<DavChangeFeed>();
        services.AddScoped<SyncFeed>();
        return marten;
    }

    /// <summary>
    /// Runs materialization: the Solo async daemon that advances the <c>scheduled_fire</c> projection, plus the
    /// nightly horizon sweep. Solo claims exclusive ownership, so exactly one host may call this — the
    /// materializer, never the API or the dispatcher (which scales on SKIP LOCKED claims).
    /// </summary>
    public static MartenServiceCollectionExtensions.MartenConfigurationExpression AddCalScheduling(
        this MartenServiceCollectionExtensions.MartenConfigurationExpression marten)
    {
        marten.Services.AddHostedService<HorizonSweep>();
        return marten.AddAsyncDaemon(DaemonMode.Solo);
    }
}
