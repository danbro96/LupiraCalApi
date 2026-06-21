using System.Text.Json.Nodes;

namespace LupiraCalApi.Dtos.Contacts;

public sealed class ContactDto
{
    public required Guid Id { get; set; }
    public required Guid AddressBookId { get; set; }
    public required string VcardUid { get; set; }
    public required string DisplayName { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? Nickname { get; set; }
    public DateOnly? Birthday { get; set; }
    public string[]? Tags { get; set; }
    public JsonNode? Metadata { get; set; }
    public required string Etag { get; set; }
}
