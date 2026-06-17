namespace LupiraCalApi.Dtos.Calendars;

/// <summary>A calendar or address book the caller can access (kind discriminates; access = the caller's grant level).</summary>
public record ContainerDto(Guid Id, string Kind, string Slug, string? DisplayName, string Access);
