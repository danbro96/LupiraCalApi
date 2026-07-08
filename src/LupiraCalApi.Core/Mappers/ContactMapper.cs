using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Contacts;
using System.Text.Json.Nodes;

namespace LupiraCalApi.Mappers;

/// <summary>Maps the <see cref="Contact"/> snapshot to its response DTO (display name is composed from the parts).
/// <paramref name="completeness"/> is computed by the service (organisation/role lives on a separate ContactGroup).</summary>
internal static class ContactMapper
{
    public static ContactDto ToResponse(this Contact c, CompletenessScore? completeness) => new()
    {
        Id = c.Id,
        AddressBookId = c.AddressBookId,
        ExternalId = c.ExternalId,
        DisplayName = c.DisplayName,
        GivenName = c.GivenName,
        FamilyName = c.FamilyName,
        Nickname = c.Nickname,
        Emails = c.Emails,
        Phones = c.Phones,
        Birthday = c.Birthday,
        Tags = c.Tags,
        Addresses = c.Addresses,
        Profiles = c.Profiles,
        Metadata = JsonNode.Parse(string.IsNullOrWhiteSpace(c.Metadata) ? "{}" : c.Metadata),
        Completeness = completeness,
        Etag = c.ContentHash,
    };
}
