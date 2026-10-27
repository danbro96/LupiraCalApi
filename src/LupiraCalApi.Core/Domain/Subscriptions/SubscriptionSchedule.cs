namespace LupiraCalApi.Core.Domain.Subscriptions;

/// <summary>When a subscription is next refreshed.</summary>
public static class SubscriptionSchedule
{
    /// <summary>One interval on, plus up to 10 % so subscriptions created together drift apart.</summary>
    public static DateTimeOffset NextAfterSuccess(DateTimeOffset now, TimeSpan interval) =>
        now + interval + (interval * (Random.Shared.NextDouble() * 0.1));

    /// <summary>Doubles the interval per consecutive failure (1-based), up to <paramref name="maxBackoff"/>.</summary>
    public static DateTimeOffset NextAfterFailure(DateTimeOffset now, TimeSpan interval, int failures, TimeSpan maxBackoff)
    {
        var factor = Math.Pow(2, Math.Clamp(failures - 1, 0, 30));
        return now + TimeSpan.FromTicks((long) Math.Min(interval.Ticks * factor, maxBackoff.Ticks));
    }

    public static bool CanRefreshManually(DateTimeOffset? lastAttempt, DateTimeOffset now, TimeSpan cooldown) =>
        lastAttempt is not { } last || now - last >= cooldown;
}
