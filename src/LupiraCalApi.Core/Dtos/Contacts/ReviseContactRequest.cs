namespace LupiraCalApi.Dtos.Contacts;

/// <summary>Update an existing contact by <b>merge</b>: a provided scalar overwrites, a provided
/// email/phone/tag array is unioned onto the existing values (deduped), and any field left null keeps
/// its current value. Enrichment never wipes what it didn't mention. The address book isn't changeable here.</summary>
public sealed class ReviseContactRequest
{
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
