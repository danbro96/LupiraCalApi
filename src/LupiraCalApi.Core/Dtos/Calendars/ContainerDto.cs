namespace LupiraCalApi.Dtos.Calendars;

/// <summary>A calendar or address book the caller can access (kind discriminates; access = "owner" | "shared").</summary>
public record ContainerDto(Guid Id, string Kind, Guid OwnerId, string Slug, string? DisplayName, string Access);
