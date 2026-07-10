using LupiraCalApi.Domain;
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

public class OwnerGrantsTests
{
    [Fact]
    public void Removing_the_only_owner_orphans() =>
        Assert.True(OwnerGrants.WouldOrphan(Access.Owner, []));

    [Fact]
    public void Removing_one_of_two_owners_does_not_orphan() =>
        Assert.False(OwnerGrants.WouldOrphan(Access.Owner, [Access.Owner]));

    [Fact]
    public void Removing_an_owner_when_only_non_owners_remain_orphans() =>
        Assert.True(OwnerGrants.WouldOrphan(Access.Owner, [Access.ReadWrite, Access.Read]));

    [Theory]
    [InlineData(Access.Read)]
    [InlineData(Access.ReadWrite)]
    public void Removing_a_non_owner_never_orphans(Access targetAccess) =>
        Assert.False(OwnerGrants.WouldOrphan(targetAccess, []));
}

public class OwnerGrantIdTests
{
    [Fact]
    public void Calendar_grant_id_is_stable_and_distinct()
    {
        var cal = Guid.NewGuid();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        Assert.Equal(CalendarOwner.MakeId(cal, p1), CalendarOwner.MakeId(cal, p1));
        Assert.NotEqual(CalendarOwner.MakeId(cal, p1), CalendarOwner.MakeId(cal, p2));
        Assert.NotEqual(CalendarOwner.MakeId(Guid.NewGuid(), p1), CalendarOwner.MakeId(Guid.NewGuid(), p1));
    }
}
