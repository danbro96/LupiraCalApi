namespace LupiraCalApi.Dtos.Calendars;

/// <summary>The result of granting a member access to a container: who now has what access on which container.</summary>
public record OwnerGrantDto(Guid ContainerId, string Kind, Guid PrincipalId, string Email, string Access);
