using Lupira.Testing.Postgres;
using LupiraCalApi.Core.Data;
using LupiraCalApi.Core.Scheduling;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LupiraCalApi.IntegrationTests;

/// <summary>The raw <c>cal.scheduled_fire</c> table and the EF subscription mirror aren't Marten-managed, so each reset
/// creates + truncates the fire table and truncates the mirror explicitly. The API host declares the scheduled_fire
/// projection but runs no daemon, so tests can drive it with RebuildProjectionAsync without one racing their per-test
/// ResetAllData.</summary>
public sealed class CalApiTestFactory : LupiraApiFactory<Program>
{
    public IDocumentStore Store => Services.GetRequiredService<IDocumentStore>();

    protected override string AuthentikSlug => "lupira-cal";

    protected override void AddSettings(IDictionary<string, string?> settings) => settings["Sync:SettleLag"] = "00:00:00";

    protected override async Task ApplySchemaAsync()
    {
        await Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>().Database.MigrateAsync();
    }

    protected override async Task ResetDataAsync()
    {
        await ScheduledFireSchema.EnsureExistsAsync(ConnectionString);
        await Store.Advanced.ResetAllData();
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"truncate table {ScheduledFireSchema.Table}, {SubscriptionsDbContext.Schema}.subscribed_event, {SubscriptionsDbContext.Schema}.subscription", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
