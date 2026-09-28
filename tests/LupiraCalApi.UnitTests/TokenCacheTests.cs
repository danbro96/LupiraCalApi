using LupiraCalApi.Clients;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class TokenCacheTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Same_key_mints_once()
    {
        var cache = new TokenCache(new FakeClock(T0));
        var mints = 0;
        Task<IssuedToken> Mint(CancellationToken _) { mints++; return Task.FromResult(new IssuedToken("a", TimeSpan.FromMinutes(5))); }

        await cache.GetOrMintAsync("k", Mint, null, default);
        await cache.GetOrMintAsync("k", Mint, null, default);

        Assert.Equal(1, mints);
    }

    [Fact]
    public void Exchange_key_hashes_the_subject_and_separates_audiences()
    {
        var cal = TokenCache.ExchangeKey("secret-token", "lupira-contact");
        var geo = TokenCache.ExchangeKey("secret-token", "lupira-geo");

        Assert.DoesNotContain("secret-token", cal);
        Assert.NotEqual(cal, geo);
    }

    [Fact]
    public async Task Entry_expires_at_the_earlier_of_skewed_expiry_and_not_after()
    {
        var clock = new FakeClock(T0);
        var cache = new TokenCache(clock);
        var mints = 0;
        Task<IssuedToken> Mint(CancellationToken _) { mints++; return Task.FromResult(new IssuedToken($"t{mints}", TimeSpan.FromMinutes(5))); }

        await cache.GetOrMintAsync("k", Mint, T0.AddMinutes(1), default);
        clock.Now = T0.AddSeconds(59);
        Assert.Equal("t1", await cache.GetOrMintAsync("k", Mint, T0.AddMinutes(1), default));
        clock.Now = T0.AddSeconds(61);
        Assert.Equal("t2", await cache.GetOrMintAsync("k", Mint, T0.AddMinutes(10), default));

        clock.Now = T0.AddSeconds(61 + 269);
        Assert.Equal("t2", await cache.GetOrMintAsync("k", Mint, null, default));
        clock.Now = T0.AddSeconds(61 + 271);
        Assert.Equal("t3", await cache.GetOrMintAsync("k", Mint, null, default));
    }

    [Fact]
    public async Task Concurrent_callers_share_one_mint()
    {
        var cache = new TokenCache(new FakeClock(T0));
        var gate = new TaskCompletionSource();
        var mints = 0;
        async Task<IssuedToken> Mint(CancellationToken _) { Interlocked.Increment(ref mints); await gate.Task; return new IssuedToken("a", TimeSpan.FromMinutes(5)); }

        var callers = Enumerable.Range(0, 20).Select(_ => cache.GetOrMintAsync("k", Mint, null, default)).ToArray();
        gate.SetResult();
        await Task.WhenAll(callers);

        Assert.Equal(1, mints);
    }

    [Fact]
    public async Task Failure_is_not_cached()
    {
        var cache = new TokenCache(new FakeClock(T0));
        var attempts = 0;
        Task<IssuedToken> Mint(CancellationToken _) => ++attempts == 1
            ? throw new TokenEndpointException(TokenErrorKind.Unavailable, null, "down")
            : Task.FromResult(new IssuedToken("a", TimeSpan.FromMinutes(5)));

        await Assert.ThrowsAsync<TokenEndpointException>(() => cache.GetOrMintAsync("k", Mint, null, default));
        Assert.Equal("a", await cache.GetOrMintAsync("k", Mint, null, default));
    }
}
