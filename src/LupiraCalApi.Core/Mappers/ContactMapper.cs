using System.Text.Json.Nodes;
using LupiraCalApi.Data.Entities;
using LupiraCalApi.Dtos.Contacts;

namespace LupiraCalApi.Mappers;

/// <summary>Maps the <see cref="Contact"/> entity to its response DTO.</summary>
internal static class ContactMapper
{
    public static ContactDto ToResponse(this Contact c) => new(
        c.Id, c.AddressBookId, c.VcardUid, c.FullName, c.Organization, c.Birthday, c.Tags,
        JsonNode.Parse(string.IsNullOrWhiteSpace(c.Metadata) ? "{}" : c.Metadata), c.ContentHash);
}
