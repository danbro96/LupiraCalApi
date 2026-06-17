namespace LupiraCalApi.Dtos.Contacts;

/// <summary>Create a contact via REST/MCP. No <c>FullName</c> — the display name is composed from the structured parts.
/// An employer is set separately as membership in an <c>organization</c>-kind contact group.</summary>
public record CreateContactRequest(
    Guid AddressBookId, string? NamePrefix, string? GivenName, string? MiddleName, string? FamilyName,
    string? NameSuffix, string? Nickname, string[]? Emails, string[]? Phones, DateOnly? Birthday, string[]? Tags);
