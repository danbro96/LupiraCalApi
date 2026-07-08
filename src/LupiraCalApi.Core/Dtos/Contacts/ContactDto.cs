using LupiraCalApi.Domain;
using System.Text.Json.Nodes;

namespace LupiraCalApi.Dtos.Contacts;

public sealed class ContactDto
{
    public required Guid Id { get; set; }
    public required Guid AddressBookId { get; set; }
    public required string ExternalId { get; set; }
    public required string DisplayName { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? Nickname { get; set; }
    public string[]? Emails { get; set; }
    public string[]? Phones { get; set; }
    public DateOnly? Birthday { get; set; }
    public string[]? Tags { get; set; }
    public required IReadOnlyList<ContactPostalAddress> Addresses { get; set; }
    public required IReadOnlyList<ContactSocialProfile> Profiles { get; set; }
    public JsonNode? Metadata { get; set; }

    /// <summary>How well-documented this contact is. Drives contact-enrichment ranking (completeness × relevance).</summary>
    public CompletenessScore? Completeness { get; set; }

    public required string Etag { get; set; }
}
