using LupiraCalApi.Core.Domain.Calendars;
using LupiraCalApi.Core.Domain.Shared;
using Xunit;

namespace LupiraCalApi.UnitTests;

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
