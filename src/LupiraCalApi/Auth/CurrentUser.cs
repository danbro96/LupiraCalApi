using System.Security.Claims;
using LupiraCalApi.Application;
using LupiraCalApi.Data.Entities;

namespace LupiraCalApi.Auth;

/// <summary>
/// The ASP.NET half of identity: reads the calling principal's claims (OIDC JWT, or the DAV Basic email)
/// and resolves them — JIT-provisioning on first login — to the local <see cref="User"/> via the Core
/// <see cref="UserDirectory"/>. Both surfaces (REST handlers + DAV) go through this so they converge on
/// one users row.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor http, UserDirectory directory)
{
    public async Task<User> GetAsync(CancellationToken ct = default)
    {
        var principal = http.HttpContext?.User
            ?? throw new InvalidOperationException("No HTTP context available.");

        var sub = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue("email") ?? principal.FindFirstValue(ClaimTypes.Email) ?? "";
        var name = principal.FindFirstValue("name") ?? principal.Identity?.Name;
        if (sub is null && string.IsNullOrEmpty(email))
            throw new InvalidOperationException("Authenticated principal has no subject or email claim.");

        return await directory.ResolveOrProvisionAsync(sub, email, name, ct);
    }
}
