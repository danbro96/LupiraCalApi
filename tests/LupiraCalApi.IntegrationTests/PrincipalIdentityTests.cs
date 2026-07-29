using LupiraCalApi.Application;
using LupiraCalApi.Domain;
using Marten;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>
/// Identity provisioning is a check-then-insert, so concurrent first-sight logins race. The unique index on
/// <c>AuthentikSub</c> lets one win and the losers adopt its row, so one login can never fork into two principals
/// — a fork silently strands everything keyed to the principal id behind whichever duplicate Postgres returns.
/// </summary>
public sealed class PrincipalIdentityTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Concurrent_first_logins_converge_on_one_principal()
    {
        const string sub = "authentik-sub-concurrent-provision";

        // Each task needs its own session — IDocumentSession is not thread-safe, so sharing one would
        // serialize the writes and never exercise the race.
        var resolved = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var s = Factory.Store.LightweightSession();
            return (await new PrincipalDirectory(s).ResolveOrProvisionAsync(sub, "racer@x.test", "Racer")).Id;
        }));

        Assert.Single(resolved.Distinct());

        await using var q = Factory.Store.QuerySession();
        var rows = await q.Query<Principal>().Where(x => x.AuthentikSub == sub).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(rows[0].Id, resolved[0]);
    }

    /// <summary>Repeated resolution returns a constant id — the property that broke when an unordered
    /// <c>FirstOrDefault</c> ran over duplicate rows.</summary>
    [Fact]
    public async Task Repeated_resolution_is_stable()
    {
        await using var s = Factory.Store.LightweightSession();
        var directory = new PrincipalDirectory(s);

        var first = await directory.ResolveOrProvisionAsync("authentik-sub-stable", "stable@x.test", "Stable");
        for (var i = 0; i < 10; i++)
            Assert.Equal(first.Id, (await directory.ResolveOrProvisionAsync("authentik-sub-stable", "stable@x.test", "Stable")).Id);
    }
}
