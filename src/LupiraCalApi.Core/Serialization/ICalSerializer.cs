using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using LupiraCalApi.Domain;
using System.Text.RegularExpressions;
using IcalCalendar = Ical.Net.Calendar;

namespace LupiraCalApi.Serialization;

/// <summary>iCalendar (VEVENT) author + parse via Ical.Net. GET serves the stored raw blob verbatim; the parsed
/// projection feeds REST/MCP queries + search. Works in primitives so it stays decoupled from the domain aggregates.
/// (Location is a free string here; the service resolves it to/from a <see cref="Place"/>.)</summary>
public static class ICalSerializer
{
    public static string ToICalendar(
        string uid, string? title, string? description, string? location, ItemStatus? status,
        bool isAllDay, DateTimeOffset? startsAt, DateTimeOffset? endsAt,
        DateOnly? startDate, DateOnly? endDate, string? recurrenceRule)
    {
        var calendar = new IcalCalendar();
        var ev = new CalendarEvent { Uid = uid };

        if (!string.IsNullOrWhiteSpace(title)) ev.Summary = title;
        if (!string.IsNullOrWhiteSpace(description)) ev.Description = description;
        if (!string.IsNullOrWhiteSpace(location)) ev.Location = location;
        if (status is { } s) ev.Status = s switch
        {
            ItemStatus.Confirmed => "CONFIRMED",
            ItemStatus.Cancelled => "CANCELLED",
            _ => "TENTATIVE",
        };

        if (isAllDay && startDate is { } sd)
        {
            ev.Start = new CalDateTime(sd.Year, sd.Month, sd.Day);
            var end = endDate ?? sd;
            ev.End = new CalDateTime(end.Year, end.Month, end.Day);
        }
        else if (startsAt is { } sa)
        {
            ev.Start = new CalDateTime(sa.UtcDateTime, "UTC");
            if (endsAt is { } ea) ev.End = new CalDateTime(ea.UtcDateTime, "UTC");
        }

        if (!string.IsNullOrWhiteSpace(recurrenceRule))
            ev.RecurrenceRule = new RecurrencePattern(recurrenceRule);

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
