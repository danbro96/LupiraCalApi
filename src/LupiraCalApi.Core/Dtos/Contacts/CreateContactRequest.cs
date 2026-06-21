namespace LupiraCalApi.Dtos.Contacts;

/// <summary>Create a contact via REST/MCP. No <c>FullName</c> — the display name is composed from the structured parts.
/// An employer is set separately as membership in an <c>organization</c>-kind contact group.</summary>
public sealed class CreateContactRequest
{
    public required Guid AddressBookId { get; set; }
    public string? NamePrefix { get; set; }
    public string? GivenName { get; set; }
    public string? MiddleName { get; set; }
    public string? FamilyName { get; set; }
    public string? NameSuffix { get; set; }
    public string? Nickname { get; set; }
    public string[]? Emails { get; set; }
    public string[]? Phones { get; set; }
    public DateOnly? Birthday { get; set; }
    public string[]? Tags { get; set; }
}
