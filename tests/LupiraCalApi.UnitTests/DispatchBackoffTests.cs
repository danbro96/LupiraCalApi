using LupiraCalApi.Scheduling;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class DispatchBackoffTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 300)]
    [InlineData(4, 900)]
    [InlineData(5, 1800)]
    public void Delay_follows_the_design_schedule(int attempts, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), DispatchBackoff.Delay(attempts));

    [Fact]
    public void Delay_clamps_below_and_above()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), DispatchBackoff.Delay(0));      // defensive: never negative-index
        Assert.Equal(TimeSpan.FromMinutes(30), DispatchBackoff.Delay(99));     // lease-reclaim overruns stay at the last step
    }
}
