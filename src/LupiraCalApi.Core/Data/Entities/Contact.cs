using NpgsqlTypes;

namespace LupiraCalApi.Data.Entities;

public class Contact
{
    public Guid Id { get; set; }
    public Guid AddressBookId { get; set; }
    public string VcardUid { get; set; } = null!;       // stable per-contact id; also the DAV resource name
    public string? FullName { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? Organization { get; set; }
    public string? Emails { get; set; }                 // jsonb
    public string? Phones { get; set; }                 // jsonb
    public string? Addresses { get; set; }              // jsonb
    public DateOnly? Birthday { get; set; }
    public string? PhotoUrl { get; set; }               // presigned MinIO link, not bytes
    public string SourceVcard { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public string[]? Tags { get; set; }
    public string Metadata { get; set; } = "{}";        // jsonb
    public NpgsqlTsVector SearchVector { get; set; } = null!; // generated (full_name+organization); GIN-indexed
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
