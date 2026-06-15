namespace LupiraCalApi.Data.Entities;

/// <summary>A family member (or the shared `family` principal). Stable id is the ownership key; email is a mutable lookup attribute.</summary>
public class User
{
    public Guid Id { get; set; }
    public string AuthentikSub { get; set; } = null!;   // durable external anchor (OIDC sub)
    public string Email { get; set; } = null!;          // lowercased; DAV login + display only
    public bool IsShared { get; set; }                  // true for the shared `family` principal
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
