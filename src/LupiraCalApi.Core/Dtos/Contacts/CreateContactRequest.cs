namespace LupiraCalApi.Dtos.Contacts;

public record CreateContactRequest(
    Guid AddressBookId, string FullName, string? GivenName, string? FamilyName, string? Organization,
    string[]? Emails, string[]? Phones, DateOnly? Birthday, string[]? Tags);
