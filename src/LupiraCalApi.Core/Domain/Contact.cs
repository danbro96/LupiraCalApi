namespace LupiraCalApi.Domain;

/// <summary>A contact's postal address: a reference to an <c>address</c>-kind <see cref="Place"/> with a vCard type.</summary>
public sealed class ContactPostalAddress
{
    public Guid PlaceId { get; set; }
    public ContactAddressType Type { get; set; }
}

/// <summary>A social/IM handle. <c>Service</c> is an open string (platforms are unbounded).</summary>
public sealed class ContactSocialProfile
{
    public string Service { get; set; } = "";
    public string Handle { get; set; } = "";
    public string? Url { get; set; }
}

/// <summary>
/// The contact (VCARD) aggregate + inline snapshot, belonging to one address book. The <c>SourceVcard</c> blob is
/// returned verbatim over CardDAV; <c>ContentHash</c> is the ETag. Postal addresses reference <see cref="Place"/>.
/// </summary>
public sealed class Contact
{
    public Guid Id { get; set; }
    public Guid AddressBookId { get; set; }
    public string VcardUid { get; set; } = "";

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

    public string SourceVcard { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string Metadata { get; set; } = "{}";

    public List<ContactPostalAddress> Addresses { get; set; } = new();
    public List<ContactSocialProfile> Profiles { get; set; } = new();
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Composed display name (no stored FN). Falls back to the nickname, then the vCard uid.</summary>
    public string DisplayName
    {
        get
        {
            var name = string.Join(' ', new[] { NamePrefix, GivenName, MiddleName, FamilyName, NameSuffix }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            return name.Length > 0 ? name : (Nickname ?? VcardUid);
        }
    }

    public void Apply(ContactCreated e)
    {
        Id = e.ContactId;
        AddressBookId = e.AddressBookId;
        VcardUid = e.VcardUid;
        SetFields(e.Fields);
        SourceVcard = e.SourceVcard;
        ContentHash = e.ContentHash;
        DeletedAt = null;
    }

    public void Apply(ContactVcardPut e)
    {
        Id = e.ContactId;
        AddressBookId = e.AddressBookId;
        VcardUid = e.VcardUid;
        SetFields(e.Parsed);
        SourceVcard = e.SourceVcard;
        ContentHash = e.ContentHash;
        DeletedAt = null;
    }

    public void Apply(ContactRevised e)
    {
        SetFields(e.Fields);
        SourceVcard = e.SourceVcard;
        ContentHash = e.ContentHash;
    }

    public void Apply(ContactDeleted _) => DeletedAt = DateTimeOffset.UtcNow;

    public void Apply(ContactRestored e)
    {
        DeletedAt = null;
        SourceVcard = e.SourceVcard;
        ContentHash = e.ContentHash;
    }

    public void Apply(ContactAddressesReplaced e) =>
        Addresses = e.Addresses.Select(a => new ContactPostalAddress { PlaceId = a.PlaceId, Type = a.Type }).ToList();

    public void Apply(ContactProfilesReplaced e) =>
        Profiles = e.Profiles.Select(p => new ContactSocialProfile { Service = p.Service, Handle = p.Handle, Url = p.Url }).ToList();

    private void SetFields(ContactFields f)
    {
        NamePrefix = f.NamePrefix;
        GivenName = f.GivenName;
        MiddleName = f.MiddleName;
        FamilyName = f.FamilyName;
        NameSuffix = f.NameSuffix;
        Nickname = f.Nickname;
        Emails = f.Emails;
        Phones = f.Phones;
        Birthday = f.Birthday;
        if (f.Tags is not null) Tags = f.Tags;
    }
}
