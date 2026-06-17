using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Contacts;
using System.Text.Json.Nodes;

namespace LupiraCalApi.Mappers;

/// <summary>Maps the <see cref="Contact"/> snapshot to its response DTO (display name is composed from the parts).</summary>
internal static class ContactMapper
{
    public static ContactDto ToResponse(this Contact c) => new(
        c.Id, c.AddressBookId, c.VcardUid, c.DisplayName, c.GivenName, c.FamilyName, c.Nickname,
        c.Birthday, c.Tags, JsonNode.Parse(string.IsNullOrWhiteSpace(c.Metadata) ? "{}" : c.Metadata), c.ContentHash);
}
