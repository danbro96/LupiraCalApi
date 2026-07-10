using LupiraCalApi.Domain;

namespace LupiraCalApi.Dtos.Contacts;

/// <summary>Upserts a directed relation edge on a contact: "<see cref="ToContactId"/> is this contact's <see cref="Kind"/>".</summary>
public sealed class AddContactRelationRequest
{
    public required Guid ToContactId { get; set; }
    public required ContactRelationKind Kind { get; set; }

    /// <summary>Free-text refinement of the kind, e.g. "dad".</summary>
    public string? Label { get; set; }
}
