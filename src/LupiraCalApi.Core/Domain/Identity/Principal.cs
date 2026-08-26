namespace LupiraCalApi.Core.Domain.Identity;

/// <summary>
/// An identity (plain document, JIT-provisioned from Authentik). <see cref="AuthentikSub"/> is the durable anchor;
/// <see cref="Email"/> is the mutable OIDC join key (also the acting-user key on the /dav-backend seam).
/// </summary>
public sealed class Principal
{
    public Guid Id { get; set; }

    public string AuthentikSub { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    /// <summary>First provisioned. Pre-existing rows carry a reconstructed estimate.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
