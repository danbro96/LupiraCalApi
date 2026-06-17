namespace LupiraCalApi.Domain;

/// <summary>Parses the wire <c>access</c> value of a sharing grant into <see cref="Access"/>. Empty defaults to
/// <see cref="Access.Owner"/> (the family-calendar case); hyphenated and bare forms both accepted, case-insensitive.</summary>
public static class AccessParsing
{
    public static (bool Ok, Access Value) Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (true, Access.Owner);
        return raw.Trim().ToLowerInvariant().Replace("-", "") switch
        {
            "owner" => (true, Access.Owner),
            "readwrite" => (true, Access.ReadWrite),
            "read" => (true, Access.Read),
            _ => (false, default),
        };
    }
}

/// <summary>Pure rules over a container's owner-membership set, independent of storage.</summary>
public static class OwnerGrants
{
    /// <summary>True if removing a grantee who currently holds <paramref name="targetAccess"/> would leave the
    /// container with no owners. <paramref name="otherGrantsAccess"/> is the access level of every OTHER grant.</summary>
    public static bool WouldOrphan(Access targetAccess, IReadOnlyCollection<Access> otherGrantsAccess) =>
        targetAccess == Access.Owner && !otherGrantsAccess.Any(a => a == Access.Owner);
}
