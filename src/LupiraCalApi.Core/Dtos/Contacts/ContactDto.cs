using System.Text.Json.Nodes;

namespace LupiraCalApi.Dtos.Contacts;

public record ContactDto(
    Guid Id, Guid AddressBookId, string VcardUid, string? FullName, string? Organization,
    DateOnly? Birthday, string[]? Tags, JsonNode? Metadata, string Etag);
