namespace LupiraCalApi.Domain;

/// <summary>
/// An identity (plain document, JIT-provisioned from Authentik). <see cref="AuthentikSub"/> is the durable anchor;
/// <see cref="Email"/> is the mutable DAV/OIDC join key. <see cref="ContactId"/> links the principal to its own
/// <see cref="Contact"/> ("my details" / "what I attended").
/// </summary>
public sealed class Principal
{
    public Guid Id { get; set; }
    public string AuthentikSub { get; set; } = "";
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    public Guid? ContactId { get; set; }
}
