using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace LupiraCalApi.Auth;

public static class DavConstants
{
    public const string Scheme = "dav";
}

/// <summary>
/// HTTP Basic auth for the /dav surface (DAV clients can't do OIDC). The decoded email becomes the
/// principal; resolution to the local users.id happens via IUserContext (by email), so it converges with
/// the member's OIDC identity.
///
/// In Development any password is accepted (login = email) so the DAV surface is testable without LDAP.
/// In other environments the password MUST be bound against the Authentik LDAP outpost — TODO(infra):
/// implement the System.DirectoryServices.Protocols bind against authentik-ldap; until then non-dev fails closed.
/// </summary>
public sealed class DavBasicAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IHostEnvironment _env;

    public DavBasicAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IHostEnvironment env)
        : base(options, logger, encoder)
    {
        _env = env;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header)) return Task.FromResult(AuthenticateResult.NoResult());
        var value = header.ToString();
        if (!value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(AuthenticateResult.NoResult());

        string email, password;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value["Basic ".Length..].Trim()));
            var sep = decoded.IndexOf(':');
            if (sep < 0) return Task.FromResult(AuthenticateResult.Fail("Malformed Basic credentials."));
            email = decoded[..sep].Trim().ToLowerInvariant();
            password = decoded[(sep + 1)..];
        }
        catch
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed Basic credentials."));
        }

        if (email.Length == 0) return Task.FromResult(AuthenticateResult.Fail("Empty username."));

        var bound = _env.IsDevelopment() ? password.Length >= 0 /* dev: accept any */ : LdapBind(email, password);
        if (!bound) return Task.FromResult(AuthenticateResult.Fail("Invalid credentials."));

        // No sub claim — IUserContext resolves/JIT-provisions by email so DAV and OIDC converge.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, email), new Claim("email", email)], DavConstants.Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, DavConstants.Scheme)));
    }

    // TODO(infra): bind against the Authentik LDAP outpost (System.DirectoryServices.Protocols,
    // reader DN search by mail, then bind with the supplied password). Fails closed until implemented.
    private static bool LdapBind(string email, string password) => false;

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Basic realm=\"lupira-cal-dav\", charset=\"UTF-8\"";
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
