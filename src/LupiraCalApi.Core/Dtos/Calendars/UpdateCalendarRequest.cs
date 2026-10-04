namespace LupiraCalApi.Core.Dtos.Calendars;

/// <summary>Changes a calendar's settings. <c>DefaultTimezone</c> is an IANA zone id; it applies to items written later
/// without a zone of their own, never to existing items.</summary>
public sealed class UpdateCalendarRequest
{
    public required string DefaultTimezone { get; set; }
}
