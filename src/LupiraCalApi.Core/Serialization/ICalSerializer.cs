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
        DateOnly? startDate, DateOnly? endDate, string? recurrenceRule,
        string? recurrenceExceptions = null, string? recurrenceOverrides = null)
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
        var ics = new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;

        // The structured model has no shape for per-instance exceptions, so EXDATE/RDATE and RECURRENCE-ID override
        // VEVENTs are re-inserted verbatim: EXDATE/RDATE into the master (the only VEVENT so far, hence the first
        // END:VEVENT), overrides just before END:VCALENDAR. Deterministic → the regenerated ICS (and its ETag) is stable.
        if (!string.IsNullOrWhiteSpace(recurrenceExceptions))
        {
            var end = ics.IndexOf("END:VEVENT", StringComparison.Ordinal);
            if (end >= 0) ics = ics.Insert(end, NormalizeCrlf(recurrenceExceptions) + "\r\n");
        }
        if (!string.IsNullOrWhiteSpace(recurrenceOverrides))
        {
            var end = ics.LastIndexOf("END:VCALENDAR", StringComparison.Ordinal);
            if (end >= 0) ics = ics.Insert(end, NormalizeCrlf(recurrenceOverrides) + "\r\n");
        }
        return ics;
    }

    static string NormalizeCrlf(string s) => s.Replace("\r\n", "\n").Replace("\n", "\r\n").TrimEnd('\r', '\n');

    /// <summary>Regenerate the canonical ICS for an item from its structured fields. <paramref name="locationLabel"/> is the
    /// item's <see cref="Place"/> name (resolved by the caller, which has the session).</summary>
    public static string From(CalendarItem i, string? locationLabel) =>
        ToICalendar(i.ExternalId, i.Title, i.Description, locationLabel, i.Status, i.IsAllDay, i.StartsAt, i.EndsAt,
            i.StartDate, i.EndDate, i.RecurrenceRule, i.RecurrenceExceptions, i.RecurrenceOverrides);

    public static ParsedEvent ParseICalendar(string raw)
    {
        IcalCalendar? calendar;
        try { calendar = IcalCalendar.Load(raw); }
        catch (Exception ex) { throw new FormatException("Invalid iCalendar payload.", ex); }
        if (calendar is null) throw new FormatException("Invalid iCalendar payload.");

        // The master is the VEVENT without a RECURRENCE-ID; its structured fields are parsed below. Per-instance
        // override VEVENTs (and EXDATE/RDATE) are captured verbatim further down since the model can't represent them.
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

        // Verbatim recurrence supplement: the master block's EXDATE/RDATE lines (fold continuations included) and any
        // RECURRENCE-ID override VEVENTs. Round-tripped opaquely so single-occurrence edits survive GET.
        var vevents = Regex.Matches(raw, @"BEGIN:VEVENT\r?\n.*?END:VEVENT", RegexOptions.Singleline).Select(x => x.Value).ToList();
        bool IsOverride(string b) => Regex.IsMatch(b, @"^RECURRENCE-ID[;:]", RegexOptions.Multiline);
        var overrideBlocks = vevents.Where(IsOverride).ToList();
        var masterBlock = vevents.FirstOrDefault(b => !IsOverride(b));

        string? exceptions = null;
        if (masterBlock is not null)
        {
            var ex = Regex.Matches(masterBlock, @"^(?:EXDATE|RDATE)[;:].*(?:\r?\n[ \t].*)*", RegexOptions.Multiline).Select(x => x.Value).ToList();
            if (ex.Count > 0) exceptions = string.Join("\n", ex);
        }
        var overrides = overrideBlocks.Count > 0 ? string.Join("\n", overrideBlocks) : null;

        return new ParsedEvent(ev.Summary, ev.Description, ev.Location, allDay,
            startsAt, endsAt, ev.Start?.TzId, ev.End?.TzId, startDate, endDate, rrule, exceptions, overrides);
    }
}
