using LupiraCalApi.Core.Domain.Shared;
using Xunit;

namespace LupiraCalApi.UnitTests;

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
