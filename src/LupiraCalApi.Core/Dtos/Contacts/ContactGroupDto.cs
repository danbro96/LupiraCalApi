namespace LupiraCalApi.Dtos.Contacts;

/// <summary>A contact group (personal grouping or organization) and its current members.</summary>
public record ContactGroupDto(Guid Id, Guid AddressBookId, string Kind, string Name, IReadOnlyList<Guid> Members);
