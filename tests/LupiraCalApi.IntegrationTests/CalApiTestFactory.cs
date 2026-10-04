using Lupira.Testing.Postgres;
using LupiraCalApi.Core.Scheduling;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LupiraCalApi.IntegrationTests;

/// <summary>The raw <c>cal.scheduled_fire</c> table isn't Marten-managed, so each reset creates + truncates it
/// explicitly. The API host declares the scheduled_fire projection but runs no daemon, so tests can drive it with
/// RebuildProjectionAsync without one racing their per-test ResetAllData.</summary>
public sealed class CalApiTestFactory : LupiraApiFactory<Program>
{
    public IDocumentStore Store => Services.GetRequiredService<IDocumentStore>();

    protected override string AuthentikSlug => "lupira-cal";

    protected override Task ApplySchemaAsync() => Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

    protected override async Task ResetDataAsync()
    {
        await ScheduledFireSchema.EnsureExistsAsync(ConnectionString);
        await Store.Advanced.ResetAllData();
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"truncate table {ScheduledFireSchema.Table}", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
