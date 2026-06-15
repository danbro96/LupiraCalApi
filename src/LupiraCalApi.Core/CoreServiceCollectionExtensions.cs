using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Data;
using LupiraCalApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the LupiraCalApi bounded context (EF Core data + transport-neutral services) into the host's DI container.</summary>
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddCalCore(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CalDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        services.AddSingleton<RecurrenceExpander>();
        services.AddScoped<AccessResolver>();
        services.AddScoped<UserDirectory>();
        services.AddScoped<CalendarService>();
        services.AddScoped<EventService>();
        services.AddScoped<ContactService>();
        services.AddScoped<RelationService>();
        return services;
    }
}
