namespace LupiraCalApi.Domain;

/// <summary>
/// An identity (plain document, JIT-provisioned from Authentik). <see cref="AuthentikSub"/> is the durable anchor;
/// <see cref="Email"/> is the mutable OIDC join key (also the acting-user key on the /dav-backend seam).
/// </summary>
public sealed class Principal
{
    public Guid Id { get; set; }
    public string AuthentikSub { get; set; } = "";
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
}
