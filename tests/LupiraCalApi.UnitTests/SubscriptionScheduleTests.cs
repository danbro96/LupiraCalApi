using LupiraCalApi.Core.Domain.Subscriptions;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class SubscriptionScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Fact]
    public void Success_waits_one_interval_plus_at_most_a_tenth()
    {
        for (var i = 0; i < 200; i++)
        {
            var next = SubscriptionSchedule.NextAfterSuccess(Now, Hour);
            Assert.InRange(next, Now + Hour, Now + (Hour * 1.1));
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    public void Failure_doubles_the_interval_per_consecutive_failure(int failures, int hours) =>
        Assert.Equal(Now + TimeSpan.FromHours(hours), SubscriptionSchedule.NextAfterFailure(Now, Hour, failures, TimeSpan.FromHours(24)));

    [Theory]
    [InlineData(6)]
    [InlineData(64)]
    [InlineData(int.MaxValue)]
    public void Failure_backoff_is_capped(int failures) =>
        Assert.Equal(Now + TimeSpan.FromHours(24), SubscriptionSchedule.NextAfterFailure(Now, Hour, failures, TimeSpan.FromHours(24)));

    [Fact]
    public void Manual_refresh_waits_out_the_cooldown_since_the_last_attempt()
    {
        var cooldown = TimeSpan.FromMinutes(1);
        Assert.True(SubscriptionSchedule.CanRefreshManually(null, Now, cooldown));
        Assert.False(SubscriptionSchedule.CanRefreshManually(Now.AddSeconds(-59), Now, cooldown));
        Assert.True(SubscriptionSchedule.CanRefreshManually(Now.AddMinutes(-1), Now, cooldown));
    }
}
