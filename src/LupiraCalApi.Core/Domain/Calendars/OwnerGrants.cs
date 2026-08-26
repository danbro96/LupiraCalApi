using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.Calendars;

/// <summary>Pure rules over a container's owner-membership set, independent of storage.</summary>
public static class OwnerGrants
{
    /// <summary>True if removing a grantee who currently holds <paramref name="targetAccess"/> would leave the
    /// container with no owners. <paramref name="otherGrantsAccess"/> is the access level of every OTHER grant.</summary>
    public static bool WouldOrphan(Access targetAccess, IReadOnlyCollection<Access> otherGrantsAccess) =>
        targetAccess == Access.Owner && !otherGrantsAccess.Any(a => a == Access.Owner);
}
