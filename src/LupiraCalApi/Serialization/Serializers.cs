using System.Globalization;
using System.Text;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using LupiraCalApi.Data;
using IcalCalendar = Ical.Net.Calendar;

namespace LupiraCalApi.Serialization;

/// <summary>
/// Maps a stored <see cref="Event"/> to a single-VEVENT VCALENDAR via Ical.Net. This is the canonical
/// <c>source_icalendar</c> when the event is authored through REST/MCP. (Phase 4 adds the inverse parse for
/// client PUTs and preserves unknown X-properties via the stored raw blob.)
/// </summary>
public static class ICalSerializer
{
    public static string ToICalendar(Event e)
    {
        var calendar = new IcalCalendar();
        var ev = new CalendarEvent { Uid = e.IcalUid };

        if (!string.IsNullOrWhiteSpace(e.Title)) ev.Summary = e.Title;
        if (!string.IsNullOrWhiteSpace(e.Description)) ev.Description = e.Description;
        if (!string.IsNullOrWhiteSpace(e.Location)) ev.Location = e.Location;

        if (e.IsAllDay && e.StartDate is { } sd)
        {
            ev.Start = new CalDateTime(sd.Year, sd.Month, sd.Day);            // VALUE=DATE
            var end = e.EndDate ?? sd;
            ev.End = new CalDateTime(end.Year, end.Month, end.Day);
        }
        else if (e.StartsAt is { } sa)
        {
            ev.Start = new CalDateTime(sa.UtcDateTime, "UTC");
            if (e.EndsAt is { } ea) ev.End = new CalDateTime(ea.UtcDateTime, "UTC");
        }

        if (!string.IsNullOrWhiteSpace(e.RecurrenceRule))
            ev.RecurrenceRule = new RecurrencePattern(e.RecurrenceRule);

        calendar.Events.Add(ev);
        return new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;
    }
}

/// <summary>
/// Minimal vCard 3.0 writer for Phase 1 (enough to store a canonical <c>source_vcard</c> and serve a basic
/// CardDAV GET). Full FolkerKinzel.VCards round-trip with X-property preservation lands with CardDAV (Phase 5).
/// </summary>
public static class VCardSerializer
{
    public static string Build(
        string uid, string fullName, string? given, string? family, string? organization,
        IEnumerable<string>? emails, IEnumerable<string>? phones, DateOnly? birthday)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCARD\r\n");
        sb.Append("VERSION:3.0\r\n");
        sb.Append("UID:").Append(Escape(uid)).Append("\r\n");
        sb.Append("FN:").Append(Escape(fullName)).Append("\r\n");
        sb.Append("N:").Append(Escape(family ?? "")).Append(';').Append(Escape(given ?? "")).Append(";;;\r\n");
        if (!string.IsNullOrWhiteSpace(organization)) sb.Append("ORG:").Append(Escape(organization)).Append("\r\n");
        foreach (var email in emails ?? []) sb.Append("EMAIL:").Append(Escape(email)).Append("\r\n");
        foreach (var phone in phones ?? []) sb.Append("TEL:").Append(Escape(phone)).Append("\r\n");
        if (birthday is { } b) sb.Append("BDAY:").Append(b.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("END:VCARD\r\n");
        return sb.ToString();
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
}
