using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Contacts;
using System.Text.Json.Nodes;

namespace LupiraCalApi.Mappers;

/// <summary>Maps the <see cref="Contact"/> snapshot to its response DTO (display name is composed from the parts).</summary>
internal static class ContactMapper
{
    public static ContactDto ToResponse(this Contact c) => new()
    {
        Id = c.Id,
        AddressBookId = c.AddressBookId,
        VcardUid = c.VcardUid,
        DisplayName = c.DisplayName,
        GivenName = c.GivenName,
        FamilyName = c.FamilyName,
        Nickname = c.Nickname,
        Birthday = c.Birthday,
        Tags = c.Tags,
        Metadata = JsonNode.Parse(string.IsNullOrWhiteSpace(c.Metadata) ? "{}" : c.Metadata),
        Etag = c.ContentHash,
    };
}
