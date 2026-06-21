namespace LupiraCalApi.Dtos.Me;

/// <summary>The resolved local identity of the caller.</summary>
public sealed class MeDto
{
    public required Guid Id { get; set; }
    public required string Email { get; set; }
    public string? DisplayName { get; set; }
}
