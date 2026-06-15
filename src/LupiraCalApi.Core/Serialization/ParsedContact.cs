namespace LupiraCalApi.Serialization;

/// <summary>Projection parsed out of a client-PUT vCard.</summary>
public sealed record ParsedContact(
    string FullName, string? GivenName, string? FamilyName, string? Organization,
    string[]? Emails, string[]? Phones, DateOnly? Birthday);
