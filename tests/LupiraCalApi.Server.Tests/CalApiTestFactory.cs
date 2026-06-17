using Marten;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Text;
using Testcontainers.PostgreSql;

namespace LupiraCalApi.Server.Tests;

/// <summary>
/// Hosts the real app against an ephemeral Postgres (Testcontainers). Runs in <c>Development</c> so the dev auth
/// handlers are wired: <c>X-Dev-User</c> for <c>/api</c> and any-password HTTP Basic for <c>/dav</c>. Marten data is
/// reset per test via <see cref="ResetAsync"/> so DAV listings and the global event sequence (sync-token) are deterministic.
/// </summary>
public sealed class CalApiTestFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private bool _schemaApplied;

    public CalApiTestFactory() => _postgres.StartAsync().GetAwaiter().GetResult();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(cfg =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
            }));
    }

    public IDocumentStore Store => Services.GetRequiredService<IDocumentStore>();

    /// <summary>Ensure the schema exists (once), then wipe all documents + events (and reset the event sequence).</summary>
    public async Task ResetAsync()
    {
        if (!_schemaApplied)
        {
            await Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            _schemaApplied = true;
        }
        await Store.Advanced.ResetAllData();
    }

    public HttpClient ApiClient(string email)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", email);
        return client;
    }

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
