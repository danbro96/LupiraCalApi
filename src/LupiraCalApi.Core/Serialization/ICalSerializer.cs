using System.Text.RegularExpressions;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using LupiraCalApi.Data.Entities;
using IcalCalendar = Ical.Net.Calendar;

namespace LupiraCalApi.Serialization;

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
