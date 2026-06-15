namespace LupiraCalApi.Serialization;

/// <summary>
/// Maps a stored <see cref="Data.Event"/> to/from a single iCalendar VEVENT (Phase 3+), using Ical.Net.
/// Dual representation: GET returns the stored <c>SourceIcalendar</c> verbatim (lossless round-trip);
/// the structured columns are the projection for REST/MCP + queries.
/// </summary>
public static class ICalSerializer
{
    // TODO(Phase 3): string ToICalendar(Event e); Event FromICalendar(string ics, Guid calendarId);
}

/// <summary>
/// Maps a stored <see cref="Data.Contact"/> to/from a single vCard (Phase 5), using FolkerKinzel.VCards
/// (which preserves unknown properties as X- props). Same dual-representation rule as events.
/// </summary>
public static class VCardSerializer
{
    // TODO(Phase 5): string ToVCard(Contact c); Contact FromVCard(string vcard, Guid addressBookId);
}
