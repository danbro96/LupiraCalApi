namespace LupiraCalApi.Dtos.Me;

/// <summary>The resolved local identity of the caller: the stable <see cref="PrincipalId"/> plus
/// current email/display name. Same identity shape (<c>principalId</c>/<c>email</c>/<c>displayName</c>)
/// the platform uses everywhere it returns a person.</summary>
public sealed class MeDto
{
    public required Guid PrincipalId { get; set; }
    public required string Email { get; set; }
    public string? DisplayName { get; set; }
}
