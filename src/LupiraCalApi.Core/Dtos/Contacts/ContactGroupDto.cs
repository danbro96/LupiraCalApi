using LupiraCalApi.Domain;
using System.Text.Json.Serialization;

namespace LupiraCalApi.Dtos.Contacts;

/// <summary>A contact group (personal grouping or organization) and its current members.</summary>
public sealed class ContactGroupDto
{
    public required Guid Id { get; set; }
    public required Guid AddressBookId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ContactGroupKind>))]
    public required ContactGroupKind Kind { get; set; }

    public required string Name { get; set; }
    public required IReadOnlyList<Guid> Members { get; set; }
}
