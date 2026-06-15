using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using LupiraCalApi.Data;
using IcalCalendar = Ical.Net.Calendar;

namespace LupiraCalApi.Serialization;

/// <summary>Projection parsed out of a client-PUT VEVENT (the raw blob remains the source of truth).</summary>
public sealed record ParsedEvent(
    string? Title, string? Description, string? Location, bool IsAllDay,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? StartTimezone, string? EndTimezone,
    DateOnly? StartDate, DateOnly? EndDate, string? RecurrenceRule);

/// <summary>iCalendar (VEVENT) author + parse via Ical.Net. GET serves the stored raw blob verbatim;
/// the parsed projection feeds REST/MCP queries + search. Unknown properties survive in the raw blob.</summary>
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
            ev.Start = new CalDateTime(sd.Year, sd.Month, sd.Day);
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

    public static ParsedEvent ParseICalendar(string raw)
    {
        IcalCalendar? calendar;
        try { calendar = IcalCalendar.Load(raw); }
        catch (Exception ex) { throw new FormatException("Invalid iCalendar payload.", ex); }
        if (calendar is null) throw new FormatException("Invalid iCalendar payload.");

        // The master is the VEVENT without a RECURRENCE-ID; overrides (if any) stay in the raw blob.
        var ev = calendar.Events.FirstOrDefault(x => x.RecurrenceIdentifier is null)
            ?? calendar.Events.FirstOrDefault()
            ?? throw new FormatException("No VEVENT in payload.");

        var allDay = ev.Start is not null && !ev.Start.HasTime;
        DateTimeOffset? startsAt = null, endsAt = null;
        DateOnly? startDate = null, endDate = null;

        if (ev.Start is { } s)
        {
            if (allDay) startDate = DateOnly.FromDateTime(s.Value);
            else startsAt = new DateTimeOffset(s.AsUtc, TimeSpan.Zero);
        }
        if (ev.End is { } e2)
        {
            if (allDay) endDate = DateOnly.FromDateTime(e2.Value);
            else endsAt = new DateTimeOffset(e2.AsUtc, TimeSpan.Zero);
        }

        var m = Regex.Match(raw, @"^RRULE:(.+)$", RegexOptions.Multiline);
        var rrule = m.Success ? m.Groups[1].Value.Trim() : null;

        return new ParsedEvent(ev.Summary, ev.Description, ev.Location, allDay,
            startsAt, endsAt, ev.Start?.TzId, ev.End?.TzId, startDate, endDate, rrule);
    }
}

/// <summary>Projection parsed out of a client-PUT vCard.</summary>
public sealed record ParsedContact(
    string FullName, string? GivenName, string? FamilyName, string? Organization,
    string[]? Emails, string[]? Phones, DateOnly? Birthday);

/// <summary>Minimal vCard 3.0 writer + line-based parser for Phase 1/4. Full FolkerKinzel.VCards
/// round-trip (X-property preservation) lands with CardDAV interop hardening (Phase 5).</summary>
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

    public static ParsedContact ParseVCard(string raw)
    {
        string? fn = null, org = null, given = null, family = null;
        DateOnly? bday = null;
        var emails = new List<string>();
        var phones = new List<string>();

        foreach (var line in raw.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            if (l.Length == 0 || l[0] == ' ' || l[0] == '\t') continue;   // skip blanks + folded continuations
            var colon = l.IndexOf(':');
            if (colon < 0) continue;
            var prop = l[..colon].Split(';')[0].ToUpperInvariant();
            var val = l[(colon + 1)..];
            switch (prop)
            {
                case "FN": fn = Unescape(val); break;
                case "ORG": org = Unescape(val.Split(';')[0]); break;
                case "N":
                    var parts = val.Split(';');
                    if (parts.Length > 0) family = Unescape(parts[0]);
                    if (parts.Length > 1) given = Unescape(parts[1]);
                    break;
                case "EMAIL": emails.Add(Unescape(val)); break;
                case "TEL": phones.Add(Unescape(val)); break;
                case "BDAY":
                    if (DateOnly.TryParse(val, CultureInfo.InvariantCulture, out var d1)) bday = d1;
                    else if (DateOnly.TryParseExact(val, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) bday = d2;
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(fn)) fn = string.Join(' ', new[] { given, family }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new ParsedContact(fn ?? "", given, family, org,
            emails.Count > 0 ? [.. emails] : null, phones.Count > 0 ? [.. phones] : null, bday);
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
    static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
}
