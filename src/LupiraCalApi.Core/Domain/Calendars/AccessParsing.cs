using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.Calendars;

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
