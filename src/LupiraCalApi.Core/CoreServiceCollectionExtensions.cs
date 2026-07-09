using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Scheduling;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the LupiraCalApi bounded context (Marten event store + document store + transport-neutral services) into the host's DI container.</summary>
public static class CoreServiceCollectionExtensions
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=lupira_cal;Username=lupira_cal_user;Password=devpassword";

    public static IServiceCollection AddCalCore(this IServiceCollection services)
    {
        // Registered before Marten so its StartAsync (create cal.scheduled_fire) completes before the async daemon starts.
        services.AddHostedService<ScheduledFireTableInitializer>();

        // Resolve the connection string lazily from IConfiguration so test hosts (WebApplicationFactory) can
        // override ConnectionStrings:Postgres before the store is built.
        services.AddMarten(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres") ?? DefaultConnectionString;
            var opts = new StoreOptions();
            opts.Connection(connectionString);
            opts.UseLupiraCal();
            return opts;
        }).UseLightweightSessions()
          // Solo for the single-instance personal deployment; tests disable the hosted daemon and drive the projection on demand.
          .AddAsyncDaemon(Environment.GetEnvironmentVariable("CAL_ASYNC_DAEMON")?.ToLowerInvariant() == "disabled" ? DaemonMode.Disabled : DaemonMode.Solo)
          .AddProjectionWithServices<ScheduledFireProjection>(ProjectionLifecycle.Async, ServiceLifetime.Singleton, "scheduled_fire");

        services.AddSingleton<IFireMaterializer, FireMaterializer>();
        services.AddHostedService<HorizonSweep>();
        services.AddSingleton<RecurrenceExpander>();
        services.AddScoped<CompletenessResolver>();
        services.AddScoped<AccessResolver>();
        services.AddScoped<PrincipalDirectory>();
        // Default: no external gazetteer → free-text locations resolve to no id (label = raw text). The host overrides
        // this with an HTTP GeoApiClient when LupiraGeoApi is configured (Geo:BaseUrl).
        services.TryAddSingleton<IGeoResolver, NullGeoResolver>();
        services.AddScoped<CalendarService>();
        services.AddScoped<CalendarItemService>();
        services.AddScoped<ContactService>();
        services.AddScoped<ContactGroupService>();
        services.AddScoped<CurationService>();
        services.AddScoped<ParticipationService>();
        services.AddScoped<RelationService>();
        return services;
    }
}
