using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace LupiraCalApi.Clients;

/// <summary>Process-wide cache of outbound tokens with one in-flight mint per key. Exchange keys hash the subject
/// token so no member bearer is held as a dictionary key; entries never outlive the subject token.</summary>
public sealed class TokenCache(TimeProvider clock)
{
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(30);
    private const int PruneThreshold = 256;

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public static string ExchangeKey(string subjectToken, string audience) =>
        $"ex:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subjectToken)))}:{audience}";

    public static string ClientCredentialsKey(IOutboundHopOptions hop) => $"cc:{hop.TokenUrl}|{hop.ClientId}|{hop.Scope}";

    public async Task<string> GetOrMintAsync(string key, Func<Task<IssuedToken>> mint, DateTimeOffset? notAfter, CancellationToken ct)
    {
        if (TryGetValid(key, out var cached)) return cached;

        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (TryGetValid(key, out cached)) return cached;
            var issued = await mint();
            var expiresAt = clock.GetUtcNow() + issued.ExpiresIn - Skew;
            if (notAfter is { } limit && limit < expiresAt) expiresAt = limit;
            if (_entries.Count >= PruneThreshold) Prune();
            _entries[key] = new Entry(issued.AccessToken, expiresAt);
            return issued.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryGetValid(string key, out string token)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > clock.GetUtcNow())
        {
            token = entry.Token;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private void Prune()
    {
        var now = clock.GetUtcNow();
        foreach (var (key, entry) in _entries)
        {
            if (entry.ExpiresAt <= now && _entries.TryRemove(key, out _)) _gates.TryRemove(key, out _);
        }
    }

    private sealed record Entry(string Token, DateTimeOffset ExpiresAt);
}
