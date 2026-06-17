namespace LupiraCalApi.Domain;

/// <summary>
/// Structured contact fields (vCard <c>N</c> parts + nickname + multi-valued email/phone). No <c>FullName</c> —
/// the display name is composed from the parts; the raw vCard <c>FN</c> is preserved verbatim in <c>SourceVcard</c>.
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
