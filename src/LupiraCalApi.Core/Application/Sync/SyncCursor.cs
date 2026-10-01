using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LupiraCalApi.Core.Application.Sync;

/// <summary>Feed cursor: <c>UpdatedSequence</c> watermark + a token of the caller's readable calendars. Grants and
/// revokes move no item's sequence, so a scope mismatch restarts the stream.</summary>
public readonly record struct SyncCursor(long Sequence, string Scope)
{
    public static string ScopeOf(IEnumerable<Guid> readableCalendarIds)
    {
        var joined = string.Join(',', readableCalendarIds.Order().Select(id => id.ToString("N")));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..16];
    }

    /// <summary>A pre-scope cursor (bare sequence) parses with an empty scope, so it restarts once.</summary>
    public static bool TryParse(string value, out SyncCursor cursor)
    {
        cursor = default;
        var dot = value.IndexOf('.');
        if (!long.TryParse(dot < 0 ? value : value[..dot], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)) return false;
        cursor = new SyncCursor(sequence, dot < 0 ? string.Empty : value[(dot + 1)..]);
        return true;
    }

    public override string ToString() => $"{Sequence}.{Scope}";
}
