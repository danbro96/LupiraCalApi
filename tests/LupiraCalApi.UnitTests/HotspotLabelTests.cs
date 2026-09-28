using LupiraCalApi.Core.Abstractions;
using LupiraCalApi.Core.Application.Hotspots;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class HotspotLabelTests
{
    [Theory]
    [InlineData("5, Kyrkogatan, Centrum, Ljungby, Kronobergs län, Sverige", "Ljungby", "Kyrkogatan 5, Ljungby")]
    [InlineData("12B, Storgatan, Växjö, Sverige", "Växjö", "Storgatan 12B, Växjö")]
    [InlineData("Lidl, 5, Kyrkogatan, Ljungby, Sverige", "Ljungby", "Lidl, Ljungby")]
    [InlineData("Ljungby, Kronobergs län, Sverige", "Ljungby", "Ljungby")]
    [InlineData("Badplats, Bolmen, Sverige", null, "Badplats")]
    public void Short_label_leads_with_the_venue_or_street(string displayName, string? locality, string expected) =>
        Assert.Equal(expected, HotspotService.ShortLabel(new GeoReverseLabel(displayName, locality)));

    [Fact]
    public void No_reverse_result_has_no_label() => Assert.Null(HotspotService.ShortLabel(null));
}
