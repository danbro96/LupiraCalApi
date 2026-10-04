namespace LupiraCalApi.Core.Application.Items;

/// <summary>Binds <c>Items</c>. <see cref="DefaultTimezone"/> is the IANA zone a timed item written without one gets when
/// none of its calendars carries a zone of its own.</summary>
public sealed class ItemTimeZoneOptions
{
    public const string SectionName = "Items";

    public string DefaultTimezone { get; set; } = "Europe/Stockholm";
}
