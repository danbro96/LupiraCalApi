using LupiraCalApi.Domain;
using Marten;
using Npgsql;

namespace LupiraCalApi.Application;

/// <summary>
/// Resolves an authenticated principal (OIDC <c>sub</c> + email, or a DAV email) to a local <see cref="Principal"/>
/// document, JIT-provisioning on first sight. Resolves by <c>sub</c> first, then email, so the OIDC and DAV logins
/// converge on one row. The host's <c>CurrentUser</c> supplies the claims; this never sees the request.
///
/// Provisioning is a check-then-insert, so two concurrent first-sight logins can both reach the insert. A unique
/// index on <c>AuthentikSub</c> lets only one win; the loser catches the violation and adopts the winner's row.
/// Without both halves the same login forks into two principals and calendar access resolves to whichever row
/// Postgres happens to return.
///
/// Resolution is side-effect-free: provenance is stamped by <see cref="EventActor"/> at the caller's own
/// resolution site, never here — this method is also used to resolve *third parties* (a calendar-grant target),
/// where stamping would attribute the write to the wrong person.
/// </summary>
public sealed class PrincipalDirectory(IDocumentSession session)
{
    /// <summary>Looks up an existing principal by login email without provisioning. Used where a missing principal
    /// is a "not found" (e.g. revoking a grant), not a reason to create a placeholder.</summary>
    public async Task<Principal?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        email = Normalize(email);
        if (email.Length == 0) return null;
        return await session.Query<Principal>().Where(x => x.Email == email).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<Principal> ResolveOrProvisionAsync(string? sub, string email, string? name, CancellationToken ct = default)
    {
        email = Normalize(email);

        var p = await FindAsync(sub, email, ct);

        if (p is null)
        {
            p = new Principal { Id = Guid.NewGuid(), AuthentikSub = sub ?? $"email|{email}", Email = email, DisplayName = name };
            session.Store(p);
            try
            {
                await session.SaveChangesAsync(ct);
                return p;
            }
            catch (Exception ex) when (IsUniqueViolation(ex))
            {
                // Lost the provisioning race: a concurrent request inserted this sub first. Adopt its row
                // rather than forking a second identity for the same login.
                session.EjectAllPendingChanges();
                p = await FindAsync(sub, email, ct);
                if (p is null) throw;
            }
        }

        var changed = false;
        if (sub is not null && p.AuthentikSub != sub && p.AuthentikSub.StartsWith("email|", StringComparison.Ordinal)) { p.AuthentikSub = sub; changed = true; }
        if (email.Length > 0 && p.Email != email) { p.Email = email; changed = true; }
        if (name is not null && p.DisplayName != name) { p.DisplayName = name; changed = true; }
        if (changed) { session.Store(p); await session.SaveChangesAsync(ct); }
        return p;
    }

    /// <summary>Resolve by <c>sub</c> then email, ordered so the result is stable if duplicate rows ever exist —
    /// an unordered <c>FirstOrDefault</c> over duplicates flips between them per request.</summary>
    private async Task<Principal?> FindAsync(string? sub, string email, CancellationToken ct)
    {
        Principal? p = null;
        if (sub is not null)
            p = await session.Query<Principal>().Where(x => x.AuthentikSub == sub).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (p is null && email.Length > 0)
            p = await session.Query<Principal>().Where(x => x.Email == email).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return p;
    }

    private static bool IsUniqueViolation(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) return true;
        return false;
    }

    /// <summary>The single normalization point for a login email. Lookup only matches if every read and
    /// write normalizes identically — a missed lowercase silently provisions a second principal.</summary>
    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
