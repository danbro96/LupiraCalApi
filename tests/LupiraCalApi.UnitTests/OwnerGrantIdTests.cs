using LupiraCalApi.Core.Domain.Calendars;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class OwnerGrantIdTests
{
    [Fact]
    public void Calendar_grant_id_is_stable_and_distinct()
    {
        var cal = Guid.NewGuid();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        Assert.Equal(CalendarOwner.MakeId(cal, p1), CalendarOwner.MakeId(cal, p1));
        Assert.NotEqual(CalendarOwner.MakeId(cal, p1), CalendarOwner.MakeId(cal, p2));
        Assert.NotEqual(CalendarOwner.MakeId(Guid.NewGuid(), p1), CalendarOwner.MakeId(Guid.NewGuid(), p1));
    }
}
