using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using Marten;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the LupiraCalApi bounded context (Marten event store + document store + transport-neutral services) into the host's DI container.</summary>
public static class CoreServiceCollectionExtensions
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=lupira_cal;Username=lupira_cal_user;Password=devpassword";

    public static IServiceCollection AddCalCore(this IServiceCollection services)
    {
        // Resolve the connection string lazily from IConfiguration so test hosts (WebApplicationFactory) can
        // override ConnectionStrings:Postgres before the store is built.
        services.AddMarten(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres") ?? DefaultConnectionString;
            var opts = new StoreOptions();
            opts.Connection(connectionString);
            opts.UseLupiraCal();
            return opts;
        }).UseLightweightSessions();

        services.AddSingleton<RecurrenceExpander>();
        services.AddScoped<AccessResolver>();
        services.AddScoped<PrincipalDirectory>();
        services.AddScoped<PlaceService>();
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
