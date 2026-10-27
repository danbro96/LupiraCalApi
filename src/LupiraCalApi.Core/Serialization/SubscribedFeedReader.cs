using System.Text.Json;
using System.Text.RegularExpressions;
using Lupira.Primitives;
using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Domain.Subscriptions;
using NodaTime;

namespace LupiraCalApi.Core.Serialization;

/// <summary>Reads a published iCalendar feed into subscribed events — the adapter edge where feed vocabulary stops.
/// Floating times are read in the feed's X-WR-TIMEZONE when it names a known zone, else in the calendar's zone.</summary>
public static class SubscribedFeedReader
{
    private static readonly Regex FeedZone = new(@"^X-WR-TIMEZONE[^:\r\n]*:(.+?)\r?$", RegexOptions.Multiline);

    public static IReadOnlyList<SubscribedEvent> Read(Guid subscriptionId, string raw, string calendarZone)
    {
        if (!raw.TrimStart('﻿', ' ', '\t', '\r', '\n').StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Not a calendar feed.");

        var zone = (FeedZone.Match(raw) is { Success: true } m ? TimeZoneIds.Find(m.Groups[1].Value.Trim()) : null)
            ?? TimeZoneIds.Find(calendarZone) ?? DateTimeZone.Utc;

        var events = new List<SubscribedEvent>();
        foreach (var p in ICalSerializer.ParseAll(raw, zone))
        {
            if (p.Status == ItemStatus.Cancelled) continue;
            if (ToSeries(p) is not { } series) continue;

            var sourceKey = p.Uid ?? ContentHash.Of($"{(object?) series.StartsAt ?? series.StartDate:O}|{series.Title}");
            events.Add(new SubscribedEvent
            {
                Id = DeterministicGuid.From($"subscription:{subscriptionId:N}:{sourceKey}"),
                SubscriptionId = subscriptionId,
                SourceKey = sourceKey,
                FirstStart = FirstStart(series),
                LastEnd = series.IsRecurring ? null : LastEnd(series),
                ContentHash = ContentHash.Of(JsonSerializer.Serialize(series, SubscribedSeries.Json)),
                Series = series,
            });
        }

        return [.. events.DistinctBy(e => e.Id)];
    }

    private static SubscribedSeries? ToSeries(ParsedEvent p)
    {
        if (p.IsAllDay ? p.StartDate is null : p.StartsAt is null) return null;
        return new SubscribedSeries
        {
            Title = p.Title?.Trim() ?? string.Empty,
            Description = p.Description,
            LocationLabel = string.IsNullOrWhiteSpace(p.Location) ? null : p.Location.Trim(),
            Status = p.Status ?? ItemStatus.Confirmed,
            IsAllDay = p.IsAllDay,
            StartsAt = p.StartsAt,
            EndsAt = p.EndsAt,
            StartTimezone = TimeZoneIds.Find(p.StartTimezone)?.Id,
            EndTimezone = TimeZoneIds.Find(p.EndTimezone)?.Id,
            StartDate = p.StartDate,
            EndDate = p.EndDate,
            RecurrenceRule = p.RecurrenceRule,
            ExcludedOccurrences = p.ExcludedOccurrences,
            ExtraOccurrences = p.ExtraOccurrences,
            OccurrenceOverrides = p.OccurrenceOverrides,
        };
    }

    // A pre-filter bound: no occurrence may start before it, moved and extra ones included.
    private static DateTimeOffset FirstStart(SubscribedSeries s)
    {
        var start = CalendarItemService.OccurrenceStart(s.Schedule())!.Value;
        var moved = (s.OccurrenceOverrides ?? []).Select(o => o.StartsAt).OfType<DateTimeOffset>();
        return moved.Concat(s.ExtraOccurrences ?? []).Append(start).Min();
    }

    private static DateTimeOffset LastEnd(SubscribedSeries s) =>
        s.IsAllDay
            ? CalendarItemService.AllDayInstant(s.EndDate ?? s.StartDate!.Value).AddDays(1)
            : s.EndsAt is { } end && end > s.StartsAt!.Value ? end : s.StartsAt!.Value;
}
