using Lupira.Primitives;
using Xunit;

namespace LupiraCalApi.UnitTests;

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
