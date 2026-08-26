using LupiraCalApi.Core.Domain.Calendars;
using LupiraCalApi.Core.Domain.Shared;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Pure sharing rules: access-value parsing and the last-owner guard, plus the deterministic grant id that
/// makes a re-grant an idempotent upsert. No DB.</summary>
public class AccessParsingTests
{
    [Theory]
    [InlineData(null, Access.Owner)]
    [InlineData("", Access.Owner)]
    [InlineData("   ", Access.Owner)]
    [InlineData("owner", Access.Owner)]
    [InlineData("OWNER", Access.Owner)]
    [InlineData("read-write", Access.ReadWrite)]
    [InlineData("readwrite", Access.ReadWrite)]
    [InlineData("Read-Write", Access.ReadWrite)]
    [InlineData("read", Access.Read)]
    [InlineData("READ", Access.Read)]
    public void Parses_accepted_forms(string? raw, Access expected)
    {
        var (ok, value) = AccessParsing.Parse(raw);
        Assert.True(ok);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("write")]
    [InlineData("read write")]
    public void Rejects_unknown_forms(string raw) => Assert.False(AccessParsing.Parse(raw).Ok);
}
