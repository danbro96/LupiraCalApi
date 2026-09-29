using System.Text.RegularExpressions;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using IcalCalendar = Ical.Net.Calendar;

namespace LupiraCalApi.Core.Serialization;

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
        IReadOnlyList<DateTimeOffset>? excludedOccurrences = null, IReadOnlyList<DateTimeOffset>? extraOccurrences = null,
        IReadOnlyList<OccurrenceOverride>? occurrenceOverrides = null)
    {
        var calendar = new IcalCalendar();
        var ev = new CalendarEvent { Uid = uid, DtStamp = StableStamp };

        if (!string.IsNullOrWhiteSpace(title)) ev.Summary = title;
        if (!string.IsNullOrWhiteSpace(description)) ev.Description = description;
        if (!string.IsNullOrWhiteSpace(location)) ev.Location = location;
        if (status is { } s) ev.Status = StatusText(s);

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

        calendar.Events.Add(ev);
        if (!string.IsNullOrWhiteSpace(recurrenceRule))
        {
            ev.RecurrenceRule = new RecurrencePattern(recurrenceRule);
            // Sorted so the regenerated ICS (and its ETag) is byte-stable.
            foreach (var x in (excludedOccurrences ?? []).Order()) ev.ExceptionDates.Add(Moment(x, isAllDay));
            foreach (var x in (extraOccurrences ?? []).Order()) ev.RecurrenceDates.Add(Moment(x, isAllDay));
            foreach (var o in (occurrenceOverrides ?? []).OrderBy(o => o.OriginalStart))
                calendar.Events.Add(OverrideEvent(uid, o, ev, isAllDay, SeriesLength(isAllDay, startsAt, endsAt, startDate, endDate)));
        }

        return new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;
    }

    private static CalendarEvent OverrideEvent(string uid, OccurrenceOverride o, CalendarEvent series, bool isAllDay, TimeSpan? length)
    {
        var start = o.StartsAt ?? o.OriginalStart;
        var end = o.EndsAt ?? (length is { } l ? start + l : null);
        var ev = new CalendarEvent
        {
            Uid = uid,
            DtStamp = StableStamp,
            RecurrenceIdentifier = new RecurrenceIdentifier(Moment(o.OriginalStart, isAllDay)),
            Start = Moment(start, isAllDay),
            Summary = o.Title ?? series.Summary,
            Description = o.Description ?? series.Description,
            Location = o.LocationLabel ?? series.Location,
            Status = o.Status is { } st ? StatusText(st) : series.Status,
        };
        if (end is { } e) ev.End = Moment(e, isAllDay);
        return ev;
    }

    // A deviation's instant in the series' own form: all-day series address occurrences by date (00:00Z).
    private static CalDateTime Moment(DateTimeOffset at, bool isAllDay) =>
        isAllDay ? new CalDateTime(DateOnly.FromDateTime(at.UtcDateTime)) : new CalDateTime(at.UtcDateTime, "UTC");

    private static DateTimeOffset Instant(CalDateTime d) =>
        d.HasTime ? new DateTimeOffset(d.AsUtc, TimeSpan.Zero) : new DateTimeOffset(d.Value.Date, TimeSpan.Zero);

    private static TimeSpan? SeriesLength(bool isAllDay, DateTimeOffset? startsAt, DateTimeOffset? endsAt, DateOnly? startDate, DateOnly? endDate) =>
        isAllDay
            ? startDate is { } sd && endDate is { } ed ? ed.ToDateTime(TimeOnly.MinValue) - sd.ToDateTime(TimeOnly.MinValue) : null
            : startsAt is { } s && endsAt is { } e ? e - s : null;

    private static string StatusText(ItemStatus s) => s switch
    {
        ItemStatus.Confirmed => "CONFIRMED",
        ItemStatus.Cancelled => "CANCELLED",
        _ => "TENTATIVE",
    };

    private static ItemStatus? ParseStatus(string? s) => s?.ToUpperInvariant() switch
    {
        "CONFIRMED" => ItemStatus.Confirmed,
        "CANCELLED" => ItemStatus.Cancelled,
        "TENTATIVE" => ItemStatus.Tentative,
        _ => null,
    };

    /// <summary>Regenerate the canonical ICS for an item from its structured fields. <paramref name="locationLabel"/> is the
    /// item's denormalized location label.</summary>
    public static string From(CalendarItem i, string? locationLabel) =>
        ToICalendar(i.ExternalId, i.Title, i.Description, locationLabel, i.Status, i.IsAllDay, i.StartsAt, i.EndsAt,
            i.StartDate, i.EndDate, i.RecurrenceRule, i.ExcludedOccurrences, i.ExtraOccurrences, i.OccurrenceOverrides);

    /// <summary>The item's ETag: the hash of its canonical ICS. The one place canonical form and its hash are defined
    /// together, so the DAV bytes served and the stored ETag can never drift.</summary>
    public static string HashOf(CalendarItem i, string? locationLabel) => ContentHash.Of(From(i, locationLabel));

    public static ParsedEvent ParseICalendar(string raw)
    {
        IcalCalendar? calendar;
        try
        {
            calendar = IcalCalendar.Load(raw);
        }
        catch (Exception ex)
        {
            throw new FormatException("Invalid iCalendar payload.", ex);
        }

        if (calendar is null) throw new FormatException("Invalid iCalendar payload.");

        // The master is the VEVENT without a RECURRENCE-ID; the others are its per-occurrence overrides.
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

        var excluded = ev.ExceptionDates.GetAllDates().Select(Instant).Distinct().Order().ToArray();
        var extra = ev.RecurrenceDates.GetAllDates().Select(Instant)
            .Concat(ev.RecurrenceDates.GetAllPeriods().Select(p => Instant(p.StartTime))).Distinct().Order().ToArray();
        var length = SeriesLength(allDay, startsAt, endsAt, startDate, endDate);
        var overrides = calendar.Events.Where(x => x.RecurrenceIdentifier is not null)
            .Select(x => ToOverride(x, ev, length)).OrderBy(o => o.OriginalStart).ToArray();

        return new ParsedEvent(ev.Summary, ev.Description, ev.Location, allDay,
            startsAt, endsAt, ev.Start?.TzId, ev.End?.TzId, startDate, endDate, rrule,
            excluded.Length > 0 ? excluded : null, extra.Length > 0 ? extra : null, overrides.Length > 0 ? overrides : null);
    }

    // Only what differs from the series is kept; null members inherit.
    private static OccurrenceOverride ToOverride(CalendarEvent o, CalendarEvent series, TimeSpan? length)
    {
        var original = Instant(o.RecurrenceIdentifier!.StartTime);
        var start = o.Start is { } s ? Instant(s) : original;
        DateTimeOffset? end = o.End is { } e ? Instant(e) : null;
        return new OccurrenceOverride(
            original,
            start != original ? start : null,
            end is { } x && (length is not { } l || x != start + l) ? x : null,
            o.Summary != series.Summary ? o.Summary : null,
            o.Description != series.Description ? o.Description : null,
            ParseStatus(o.Status) is { } st && st != ParseStatus(series.Status) ? st : null,
            o.Location != series.Location ? o.Location : null);
    }
}
