using LupiraCalApi.Core.Domain.Shared;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class TimeZoneIdsTests
{
    [Theory]
    [InlineData("Europe/Stockholm", true)]
    [InlineData("America/New_York", true)]
    [InlineData("UTC", true)]
    [InlineData("W. Europe Standard Time", false)]
    [InlineData("Mars/Olympus", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsIana_accepts_tzdb_ids_only(string? id, bool expected) => Assert.Equal(expected, TimeZoneIds.IsIana(id));
}
