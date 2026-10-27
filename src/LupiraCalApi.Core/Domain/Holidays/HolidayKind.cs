using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.Holidays;

/// <summary>A public holiday is a red day (allmän helgdag); an eve is a customary day off that isn't one.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HolidayKind>))]
public enum HolidayKind
{
    PublicHoliday,
    Eve,
}
