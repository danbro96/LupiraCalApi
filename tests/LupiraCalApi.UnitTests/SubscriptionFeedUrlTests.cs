using LupiraCalApi.Core.Application.Subscriptions;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class SubscriptionFeedUrlTests
{
    [Theory]
    [InlineData("webcal://calendar.example.com/a.ics", "https://calendar.example.com/a.ics")]
    [InlineData("WEBCALS://calendar.example.com/a.ics", "https://calendar.example.com/a.ics")]
    [InlineData("  https://calendar.example.com/a.ics?k=1  ", "https://calendar.example.com/a.ics?k=1")]
    [InlineData("http://calendar.example.com/a.ics", "http://calendar.example.com/a.ics")]
    public void Accepts_http_https_and_webcal(string raw, string expected)
    {
        Assert.True(SubscriptionFeedUrl.TryNormalize(raw, out var url, out _));
        Assert.Equal(expected, url.AbsoluteUri);
    }

    [Theory]
    [InlineData("ftp://calendar.example.com/a.ics")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("calendar.example.com/a.ics")]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_other_schemes_and_relative_or_empty_addresses(string raw) =>
        Assert.False(SubscriptionFeedUrl.TryNormalize(raw, out _, out _));

    [Theory]
    [InlineData("https://user:secret@calendar.example.com/a.ics")]
    [InlineData("webcal://user@calendar.example.com/a.ics")]
    public void Rejects_a_user_name_or_password(string raw)
    {
        Assert.False(SubscriptionFeedUrl.TryNormalize(raw, out _, out var error));
        Assert.Contains("user name or password", error);
    }

    [Fact]
    public void Rejects_an_address_over_the_length_limit()
    {
        var at = "https://calendar.example.com/" + new string('a', SubscriptionFeedUrl.MaxLength - 29);
        Assert.True(SubscriptionFeedUrl.TryNormalize(at, out _, out _));
        Assert.False(SubscriptionFeedUrl.TryNormalize(at + "a", out _, out _));
    }

    [Fact]
    public void Source_host_is_only_the_host()
    {
        Assert.True(SubscriptionFeedUrl.TryNormalize("https://calendar.google.com/calendar/ical/x/private-secret/basic.ics", out var url, out _));
        Assert.Equal("calendar.google.com", SubscriptionFeedUrl.SourceHost(url));
    }
}
