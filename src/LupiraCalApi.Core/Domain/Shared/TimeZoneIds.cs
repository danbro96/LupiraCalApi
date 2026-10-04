using NodaTime;

namespace LupiraCalApi.Core.Domain.Shared;

/// <summary>Resolves a stored zone id to a tzdb zone; Windows ids (sent by some clients) map to their IANA equivalent.</summary>
public static class TimeZoneIds
{
    public static DateTimeZone? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(id);
        if (zone is null && TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana)) zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(iana);
        return zone;
    }

    public static bool IsIana(string? id) => !string.IsNullOrWhiteSpace(id) && DateTimeZoneProviders.Tzdb.GetZoneOrNull(id) is not null;
}
