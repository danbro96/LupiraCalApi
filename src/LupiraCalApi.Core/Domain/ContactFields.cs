namespace LupiraCalApi.Domain;

/// <summary>
/// Structured contact fields (name parts + nickname + multi-valued email/phone). No <c>FullName</c> — the display
/// name is composed from the parts, and the canonical vCard is regenerated from these fields (no raw blob is stored).
/// </summary>
public sealed record ContactFields(
    string? NamePrefix,
    string? GivenName,
    string? MiddleName,
    string? FamilyName,
    string? NameSuffix,
    string? Nickname,
    string[]? Emails,
    string[]? Phones,
    DateOnly? Birthday,
    string[]? Tags);
