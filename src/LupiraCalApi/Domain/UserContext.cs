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

        var sub = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = (principal.FindFirstValue("email") ?? principal.FindFirstValue(ClaimTypes.Email) ?? "")
            .Trim().ToLowerInvariant();
        var name = principal.FindFirstValue("name") ?? principal.Identity?.Name;
        if (sub is null && email.Length == 0) throw new AccessDeniedException("Token has no subject or email.");

        // Resolve by the stable sub first, then by email — so the OIDC identity and the DAV Basic login
        // (which carries email, not sub) converge on the same row. Email is the unique join key.
        User? user = null;
        if (sub is not null) user = await db.Users.FirstOrDefaultAsync(u => u.AuthentikSub == sub, ct);
        if (user is null && email.Length > 0) user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            user = new User { Id = Guid.NewGuid(), AuthentikSub = sub ?? $"email|{email}", Email = email, DisplayName = name };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            return user;
        }

        // Adopt a real sub over an email-placeholder (DAV-first then OIDC), and refresh mutable attributes.
        var changed = false;
        if (sub is not null && user.AuthentikSub != sub && user.AuthentikSub.StartsWith("email|", StringComparison.Ordinal))
        {
            user.AuthentikSub = sub; changed = true;
        }
        if (email.Length > 0 && user.Email != email) { user.Email = email; changed = true; }
        if (name is not null && user.DisplayName != name) { user.DisplayName = name; changed = true; }
        if (changed) await db.SaveChangesAsync(ct);
        return user;
    }
}
