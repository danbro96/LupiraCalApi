using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LupiraCalApi.Core.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations</c> can build the context without booting the host. Uses a local dev
/// connection (overridable via <c>CAL_DESIGN_CONNECTION</c>) — migrations are authored offline and applied only to
/// Testcontainers / a local PG, never the live DB.
/// </summary>
public sealed class SubscriptionsDbContextFactory : IDesignTimeDbContextFactory<SubscriptionsDbContext>
{
    public SubscriptionsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("CAL_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=lupira_cal;Username=lupira_cal_user;Password=devpassword";

        var options = new DbContextOptionsBuilder<SubscriptionsDbContext>()
            .UseNpgsql(connection, o => o.MigrationsHistoryTable("__ef_migrations_history", SubscriptionsDbContext.Schema))
            .Options;

        return new SubscriptionsDbContext(options);
    }
}
