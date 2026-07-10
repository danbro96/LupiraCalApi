using LupiraCalApi.Domain;
using System.Text.Json.Serialization;

namespace LupiraCalApi.Dtos.Contacts;

[JsonConverter(typeof(JsonStringEnumConverter<ContactRelationDirection>))]
public enum ContactRelationDirection { Outgoing, Incoming }

/// <summary>One resolved relation as seen from the viewed contact: <see cref="Kind"/> is always the OTHER contact's role
/// relative to the viewed one (incoming edges show the derived inverse kind, and their label — the other side's phrasing — is omitted).</summary>
public sealed class ContactRelationEntryDto
{
    public required Guid ContactId { get; set; }
    public required string DisplayName { get; set; }
    public required ContactRelationKind Kind { get; set; }
    public string? Label { get; set; }
    public required ContactRelationDirection Direction { get; set; }
}
