using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Auth;

public static class DavConstants
{
    public const string Scheme = "dav";
}

/// <summary>
/// HTTP Basic auth for the /dav surface. DAV clients can't do OIDC, so this binds the supplied
/// credentials against the Authentik LDAP outpost.
///
/// Phase 3: decode the Basic header, search the outpost for (mail={0}), bind with the supplied password,
/// resolve to the local users.id (via the stable Authentik attribute; fallback unique email), and build a
/// ClaimsPrincipal whose name claim is users.id. For now it parses the header and fails closed.
/// </summary>
public sealed class DavBasicAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public DavBasicAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header))
            return Task.FromResult(AuthenticateResult.NoResult());

        var value = header.ToString();
        if (!value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        // TODO(Phase 3): base64-decode "email:password", LDAP-bind against authentik-ldap, resolve users.id.
        return Task.FromResult(AuthenticateResult.Fail("DAV LDAP bind not implemented yet (Phase 3)."));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Basic realm=\"lupira-cal-dav\", charset=\"UTF-8\"";
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
