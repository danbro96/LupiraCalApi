using LupiraCalApi.Core.Data;
using LupiraCalApi.Core.Domain.Subscriptions;
using LupiraCalApi.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Published feeds for tests, and a subscription mirrored straight into the store without a fetch.</summary>
internal static class SubscriptionSeed
{
    public static string Feed(params string[] vevents) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//feed-test//EN\r\n" + string.Concat(vevents) + "END:VCALENDAR\r\n";

    public static string Event(string uid, string summary, DateTimeOffset startUtc, string? rrule = null, string? description = null) =>
        $"BEGIN:VEVENT\r\nUID:{uid}\r\nSUMMARY:{summary}\r\nDTSTART:{Stamp(startUtc)}\r\nDTEND:{Stamp(startUtc.AddHours(1))}\r\n"
        + (rrule is null ? string.Empty : $"RRULE:{rrule}\r\n")
        + (description is null ? string.Empty : $"DESCRIPTION:{description}\r\n")
        + "END:VEVENT\r\n";

    public static async Task<CalendarSubscription> SeedAsync(CalApiTestFactory factory, Guid calendarId, string feed, string name = "Feed")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        var now = DateTimeOffset.UtcNow;
        var sub = new CalendarSubscription
        {
            Id = Guid.NewGuid(),
            CalendarId = calendarId,
            DisplayName = name,
            FeedUrl = "https://feeds.test/" + Guid.NewGuid().ToString("N") + ".ics",
            CreatedAt = now,
            NextRefreshAt = now.AddHours(1),
            LastAttemptAt = now,
            LastSyncedAt = now,
        };
        var events = SubscribedFeedReader.Read(sub.Id, feed, "UTC");
        sub.EventCount = events.Count;
        db.Subscriptions.Add(sub);
        db.Events.AddRange(events);
        await db.SaveChangesAsync();
        return sub;
    }

    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
}
