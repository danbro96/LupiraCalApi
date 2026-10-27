using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Application.Subscriptions;
using LupiraCalApi.Core.Domain.Subscriptions;
using LupiraCalApi.Core.Dtos.Subscriptions;

namespace LupiraCalApi.Core.Mappers;

/// <summary>Maps subscriptions and their events to response DTOs. The feed address never leaves: only its host does.</summary>
internal static class SubscriptionMapper
{
    public static SubscriptionDto ToResponse(this CalendarSubscription s) => new()
    {
        Id = s.Id,
        CalendarId = s.CalendarId,
        DisplayName = s.DisplayName,
        Color = s.Color,
        SourceHost = SubscriptionFeedUrl.SourceHost(new Uri(s.FeedUrl)),
        NextRefreshAt = s.NextRefreshAt,
        LastAttemptAt = s.LastAttemptAt,
        LastSyncedAt = s.LastSyncedAt,
        LastError = s.LastError,
        EventCount = s.EventCount,
    };

    public static SubscribedEventDto ToResponse(this SubscribedEvent e, CalendarSubscription s)
    {
        var series = e.Series;
        return new SubscribedEventDto
        {
            Id = e.Id,
            SubscriptionId = s.Id,
            SubscriptionName = s.DisplayName,
            CalendarId = s.CalendarId,
            Title = series.Title,
            Description = series.Description,
            LocationLabel = series.LocationLabel,
            IsAllDay = series.IsAllDay,
            Start = CalendarItemService.OccurrenceStart(series.Schedule()) ?? e.FirstStart,
            End = series.IsAllDay
                ? series.EndDate is { } last ? CalendarItemService.AllDayInstant(last) : null
                : series.EndsAt,
            Status = series.Status,
            IsRecurring = series.IsRecurring,
        };
    }
}
