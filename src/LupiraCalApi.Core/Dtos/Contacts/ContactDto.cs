using System.Text.Json.Nodes;

namespace LupiraCalApi.Dtos.Contacts;

public record ContactDto(
    Guid Id, Guid AddressBookId, string VcardUid, string DisplayName, string? GivenName, string? FamilyName,
    string? Nickname, DateOnly? Birthday, string[]? Tags, JsonNode? Metadata, string Etag);
