using LupiraCalApi.Auth;
using System.Text;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Fast unit tests for the security-sensitive auth parsing in <c>DavBasicAuthHandler</c>. These paths are
/// unreachable from the integration suite (it runs in Development, which bypasses LDAP and only sends well-formed
/// <c>email:x</c> credentials), so the malformed-input and LDAP-injection branches are only covered here.</summary>
public class BasicAuthTests
{
    static string Basic(string raw) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));

    // ---------- TryParseBasicCredentials ----------

    [Fact]
    public void Valid_header_yields_lowercased_email_and_password()
    {
        Assert.True(DavBasicAuthHandler.TryParseBasicCredentials(Basic("Alice@X.test:secret"), out var email, out var password));
        Assert.Equal("alice@x.test", email);
        Assert.Equal("secret", password);
    }

    [Fact]
    public void Password_is_split_on_the_first_colon_only()
    {
        Assert.True(DavBasicAuthHandler.TryParseBasicCredentials(Basic("user@x:a:b:c"), out _, out var password));
        Assert.Equal("a:b:c", password);
    }

    [Fact]
    public void Missing_colon_separator_fails() =>
        Assert.False(DavBasicAuthHandler.TryParseBasicCredentials(Basic("nocolon"), out _, out _));

    [Fact]
    public void Invalid_base64_fails() =>
        Assert.False(DavBasicAuthHandler.TryParseBasicCredentials("Basic !!!not-base64!!!", out _, out _));

    [Fact]
    public void Non_basic_scheme_fails() =>
        Assert.False(DavBasicAuthHandler.TryParseBasicCredentials("Bearer abc.def.ghi", out _, out _));

    [Theory]
    [InlineData(":secret")]   // empty email
    [InlineData("user@x:")]   // empty password
    public void Empty_email_or_password_fails(string raw) =>
        Assert.False(DavBasicAuthHandler.TryParseBasicCredentials(Basic(raw), out _, out _));

    // ---------- EscapeLdapFilter (RFC 4515 — injection prevention) ----------

    [Theory]
    [InlineData("*", "\\2a")]
    [InlineData("(", "\\28")]
    [InlineData(")", "\\29")]
    [InlineData("\\", "\\5c")]
    [InlineData("\0", "\\00")]
    public void EscapeLdapFilter_escapes_each_special_character(string input, string expected) =>
        Assert.Equal(expected, DavBasicAuthHandler.EscapeLdapFilter(input));

    [Fact]
    public void EscapeLdapFilter_neutralizes_a_filter_injection_attempt()
    {
        var escaped = DavBasicAuthHandler.EscapeLdapFilter("*)(uid=*");
        Assert.DoesNotContain('*', escaped);
        Assert.DoesNotContain('(', escaped);
        Assert.DoesNotContain(')', escaped);
    }

    // ---------- ParseLdapUri ----------

    [Theory]
    [InlineData("ldap://host:3389", "host", 3389)]
    [InlineData("ldaps://host:636", "host", 636)]
    [InlineData("ldap://host", "host", 389)]   // no port → default 389
    public void ParseLdapUri_reads_host_and_port(string uri, string host, int port)
    {
        var (h, p) = DavBasicAuthHandler.ParseLdapUri(uri);
        Assert.Equal(host, h);
        Assert.Equal(port, p);
    }
}
