using LupiraCalApi.Domain;
using System.Text.Json.Serialization;

namespace LupiraCalApi.Dtos.Contacts;

[JsonConverter(typeof(JsonStringEnumConverter<ContactRelationDirection>))]
public enum ContactRelationDirection { Outgoing, Incoming }

/// <summary>One resolved relation as seen from the viewed contact: <see cref="Kind"/> is always the OTHER contact's role
/// relative to the viewed one (incoming edges show the derived inverse kind, and their label — the other side's phrasing — is omitted).
/// <see cref="Provenance"/> distinguishes stored edges from kin derived off the parent/child graph (returned only when inferred relations are requested).</summary>
public sealed class ContactRelationEntryDto
{
    public required Guid ContactId { get; set; }
    public required string DisplayName { get; set; }
    public required KinshipKind Kind { get; set; }
    public string? Label { get; set; }
    public required ContactRelationDirection Direction { get; set; }
    public RelationProvenance Provenance { get; set; } = RelationProvenance.Explicit;
}
