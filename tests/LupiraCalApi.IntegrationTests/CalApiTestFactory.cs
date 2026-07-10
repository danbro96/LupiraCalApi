using LupiraCalApi.Scheduling;
using Marten;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Net.Http.Headers;
using System.Text;
using Testcontainers.PostgreSql;

namespace LupiraCalApi.IntegrationTests;

/// <summary>
/// Hosts the real app against an ephemeral Postgres (Testcontainers). Runs in <c>Development</c> so the dev auth
/// handlers are wired: <c>X-Dev-User</c> for <c>/api</c> and any-password HTTP Basic for <c>/dav</c>. Marten data is
/// reset per test via <see cref="ResetAsync"/> so DAV listings and the global event sequence (sync-token) are deterministic.
/// </summary>
public sealed class CalApiTestFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private bool _schemaApplied;

    public CalApiTestFactory()
    {
        // Tests drive the scheduled_fire projection on demand (RebuildProjectionAsync) — a hosted daemon racing with the
        // per-test ResetAllData makes projection waits flaky. Set before the host builds so AddCalCore reads it.
        Environment.SetEnvironmentVariable("CAL_ASYNC_DAEMON", "disabled");
        _postgres.StartAsync().GetAwaiter().GetResult();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(cfg =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                // Never contacted (tests auth via X-Dev-User) — feeds the RFC 9728 metadata + JWT challenge.
                ["Auth:Authority"] = "https://auth.test/application/o/lupira-cal/",
            }));
    }

    public IDocumentStore Store => Services.GetRequiredService<IDocumentStore>();

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>Ensure the schema exists (once), then wipe all documents + events (and reset the event sequence).
    /// The raw <c>cal.scheduled_fire</c> table isn't Marten-managed, so it's created + truncated explicitly.</summary>
    public async Task ResetAsync()
    {
        if (!_schemaApplied)
        {
            await Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            _schemaApplied = true;
        }
        await ScheduledFireSchema.EnsureExistsAsync(ConnectionString);
        await Store.Advanced.ResetAllData();
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"truncate table {ScheduledFireSchema.Table}", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public HttpClient ApiClient(string email)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", email);
        return client;
    }

    /// <summary>A client with no auth header — for asserting unauthenticated requests are rejected.</summary>
    public HttpClient AnonymousClient() => CreateClient();

    public HttpClient DavClient(string email)
    {
        var client = CreateClient();
        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _postgres.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
