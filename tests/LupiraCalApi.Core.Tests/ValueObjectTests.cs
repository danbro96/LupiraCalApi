using LupiraCalApi.Domain;
using Xunit;

namespace LupiraCalApi.Core.Tests;

/// <summary>Deterministic stream IDs (stable across calls) and the ETag content hash.</summary>
public class DeterministicGuidTests
{
    [Fact]
    public void Stable_for_the_same_uid() =>
        Assert.Equal(DeterministicGuid.From("uid@cal.lupira.com"), DeterministicGuid.From("uid@cal.lupira.com"));

    [Fact]
    public void Differs_for_different_uids() =>
        Assert.NotEqual(DeterministicGuid.From("a@x"), DeterministicGuid.From("b@x"));
}

public class ContentHashTests
{
    [Fact]
    public void Same_content_hashes_identically() =>
        Assert.Equal(ContentHash.Of("BEGIN:VCARD"), ContentHash.Of("BEGIN:VCARD"));

    [Fact]
    public void Different_content_hashes_differently() =>
        Assert.NotEqual(ContentHash.Of("a"), ContentHash.Of("b"));

    [Fact]
    public void Hash_is_lowercase_hex_sha256()
    {
        var hash = ContentHash.Of("x");
        Assert.Equal(64, hash.Length);                          // SHA-256 → 32 bytes → 64 hex chars
        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.All(hash, ch => Assert.Contains(ch, "0123456789abcdef"));
    }
}
