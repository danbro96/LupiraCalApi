using System.Net;
using LupiraCalApi.Core.Application.Subscriptions;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class PrivateAddressGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.0.1")]
    [InlineData("::1")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:192.168.1.1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("64:ff9b::10.0.0.1")]
    public void Refuses_private_loopback_link_local_and_reserved_addresses(string address) =>
        Assert.False(PrivateAddressGuard.IsAllowed(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::8.8.8.8")]
    public void Allows_public_addresses(string address) =>
        Assert.True(PrivateAddressGuard.IsAllowed(IPAddress.Parse(address)));

    [Theory]
    [InlineData("http://127.0.0.1:9/feed.ics")]
    [InlineData("http://[::1]:9/feed.ics")]
    [InlineData("http://localhost:9/feed.ics")]
    public async Task A_fetch_to_a_refused_host_fails_before_connecting(string url)
    {
        using var http = new HttpClient(new SocketsHttpHandler { ConnectCallback = PrivateAddressGuard.ConnectAsync, UseProxy = false });

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(url));

        Assert.IsType<FeedBlockedException>(ex.GetBaseException());
    }
}
