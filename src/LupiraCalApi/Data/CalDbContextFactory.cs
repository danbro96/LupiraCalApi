using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LupiraCalApi.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations</c> can build the context without booting the web host
/// (no DB connection is opened at design time). The runtime connection string comes from configuration.
/// </summary>
public sealed class CalDbContextFactory : IDesignTimeDbContextFactory<CalDbContext>
{
    public CalDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CalDbContext>()
            .UseNpgsql("Host=localhost;Database=lupira_cal;Username=lupira_cal_user;Password=design-time")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new CalDbContext(options);
    }
}
