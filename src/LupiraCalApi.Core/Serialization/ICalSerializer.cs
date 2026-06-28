using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using LupiraCalApi.Domain;
using System.Text.RegularExpressions;
using IcalCalendar = Ical.Net.Calendar;

namespace LupiraCalApi.Serialization;

/// <summary>iCalendar (VEVENT) author + parse via Ical.Net. The structured fields are canonical: GET regenerates the ICS on
/// demand from them, and the ETag is derived from that generated form — so generation must be deterministic (fixed DTSTAMP,
/// no wall-clock fields). Works in primitives so it stays decoupled from the domain aggregates.</summary>
public static class ICalSerializer
{
    // Fixed so regenerated ICS is byte-stable across reads (the ETag derives from it). DTSTAMP is meaningless for a
    // server-regenerated projection; the canonical state is the structured fields.
    private static readonly CalDateTime StableStamp = new(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), "UTC");

    public static string ToICalendar(
        string uid, string? title, string? description, string? location, ItemStatus? status,
        bool isAllDay, DateTimeOffset? startsAt, DateTimeOffset? endsAt,
        DateOnly? startDate, DateOnly? endDate, string? recurrenceRule)
    {
        var calendar = new IcalCalendar();
        var ev = new CalendarEvent { Uid = uid, DtStamp = StableStamp };

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

    /// <summary>Regenerate the canonical ICS for an item from its structured fields. <paramref name="locationLabel"/> is the
    /// item's <see cref="Place"/> name (resolved by the caller, which has the session).</summary>
    public static string From(CalendarItem i, string? locationLabel) =>
        ToICalendar(i.IcalUid, i.Title, i.Description, locationLabel, i.Status, i.IsAllDay, i.StartsAt, i.EndsAt, i.StartDate, i.EndDate, i.RecurrenceRule);

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
