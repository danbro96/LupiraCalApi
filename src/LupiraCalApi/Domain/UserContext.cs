using System.Security.Claims;
using LupiraCalApi.Data;
using Microsoft.EntityFrameworkCore;

namespace LupiraCalApi.Domain;

public interface IUserContext
{
    /// <summary>Resolves the calling principal to a local user row, JIT-provisioning on first login (keyed by the OIDC sub).</summary>
    Task<User> GetCurrentUserAsync(CancellationToken ct = default);
}

public sealed class UserContext(IHttpContextAccessor http, CalDbContext db) : IUserContext
{
    public async Task<User> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var principal = http.HttpContext?.User
            ?? throw new InvalidOperationException("No HTTP context available.");

        var sub = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new AccessDeniedException("Token has no subject claim.");
        var email = (principal.FindFirstValue("email") ?? principal.FindFirstValue(ClaimTypes.Email) ?? "")
            .Trim().ToLowerInvariant();
        var name = principal.FindFirstValue("name") ?? principal.Identity?.Name;

        var user = await db.Users.FirstOrDefaultAsync(u => u.AuthentikSub == sub, ct);
        if (user is null)
        {
            user = new User { Id = Guid.NewGuid(), AuthentikSub = sub, Email = email, DisplayName = name };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            return user;
        }

        // Refresh mutable attributes from the token (email/display name can change in Authentik).
        var changed = false;
        if (email.Length > 0 && user.Email != email) { user.Email = email; changed = true; }
        if (name is not null && user.DisplayName != name) { user.DisplayName = name; changed = true; }
        if (changed) await db.SaveChangesAsync(ct);
        return user;
    }
}
