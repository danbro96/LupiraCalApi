namespace LupiraCalApi.Dtos.Me;

/// <summary>The resolved local identity of the caller.</summary>
public record MeDto(Guid Id, string Email, string? DisplayName);
