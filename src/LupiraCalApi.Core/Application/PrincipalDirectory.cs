using LupiraCalApi.Domain;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>
/// Resolves an authenticated principal (OIDC <c>sub</c> + email, or a DAV email) to a local <see cref="Principal"/>
/// document, JIT-provisioning on first sight. Resolves by <c>sub</c> first, then email, so the OIDC and DAV logins
/// converge on one row. The host's <c>CurrentUser</c> supplies the claims; this never sees the request.
/// </summary>
public sealed class PrincipalDirectory(IDocumentSession session)
{
    public async Task<Principal> ResolveOrProvisionAsync(string? sub, string email, string? name, CancellationToken ct = default)
    {
        email = email.Trim().ToLowerInvariant();

        Principal? p = null;
        if (sub is not null) p = await session.Query<Principal>().FirstOrDefaultAsync(x => x.AuthentikSub == sub, ct);
        if (p is null && email.Length > 0) p = await session.Query<Principal>().FirstOrDefaultAsync(x => x.Email == email, ct);

        if (p is null)
        {
            p = new Principal { Id = Guid.NewGuid(), AuthentikSub = sub ?? $"email|{email}", Email = email, DisplayName = name };
            session.Store(p);
            await session.SaveChangesAsync(ct);
            return p;
        }

        var changed = false;
        if (sub is not null && p.AuthentikSub != sub && p.AuthentikSub.StartsWith("email|", StringComparison.Ordinal)) { p.AuthentikSub = sub; changed = true; }
        if (email.Length > 0 && p.Email != email) { p.Email = email; changed = true; }
        if (name is not null && p.DisplayName != name) { p.DisplayName = name; changed = true; }
        if (changed) { session.Store(p); await session.SaveChangesAsync(ct); }
        return p;
    }
}
