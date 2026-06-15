using LupiraCalApi.Data;
using LupiraCalApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LupiraCalApi.Application;

/// <summary>
/// Resolves an authenticated principal (OIDC <c>sub</c> + email, or a DAV email) to a local <see cref="User"/>,
/// JIT-provisioning on first sight. Resolves by <c>sub</c> first, then by email, so the OIDC identity and the
/// DAV Basic login converge on the same row (email is the unique join key). Pure EF — the host's
/// <c>CurrentUser</c> supplies the claims; this never sees the request.
/// </summary>
public sealed class UserDirectory(CalDbContext db)
{
    public async Task<User> ResolveOrProvisionAsync(string? sub, string email, string? name, CancellationToken ct = default)
    {
        email = email.Trim().ToLowerInvariant();

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
