using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Auth;

/// <summary>
/// DEVELOPMENT-ONLY auth: authenticates as the member named in the <c>X-Dev-User</c> header (an email), so the
/// API can be exercised locally without Authentik. Registered only when the environment is Development.
/// </summary>
public sealed class DevAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Dev";

    public DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Dev-User", out var value) || string.IsNullOrWhiteSpace(value))
            return Task.FromResult(AuthenticateResult.NoResult());

        var email = value.ToString().Trim().ToLowerInvariant();
        var claims = new List<Claim>
        {
            new("sub", "dev|" + email),
            new("email", email),
            new("name", email),
        };
        if (Request.Headers.TryGetValue("X-Dev-Scopes", out var scopes) && !string.IsNullOrWhiteSpace(scopes))
            claims.Add(new Claim("scope", scopes.ToString()));
        if (Request.Headers.TryGetValue("X-Dev-Client", out var client) && !string.IsNullOrWhiteSpace(client))
            claims.Add(new Claim("azp", client.ToString()));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
