namespace LupiraCalApi.Domain;

/// <summary>
/// A named collection of contacts in an address book + inline snapshot. <see cref="Kind"/> distinguishes a personal
/// grouping (Friends/Family/Colleagues) from an <c>Organization</c> (a company — a contact's employer is membership
/// here, not a free-text field). Membership add/remove are events, so "when X joined" is retained as history.
/// </summary>
public sealed class ContactGroup
{
    public Guid Id { get; set; }
    public Guid AddressBookId { get; set; }
    public ContactGroupKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? GroupVcardUid { get; set; }
    public List<Guid> MemberContactIds { get; set; } = new();
    public DateTimeOffset? DeletedAt { get; set; }

    public void Apply(ContactGroupCreated e)
    {
        Id = e.GroupId;
        AddressBookId = e.AddressBookId;
        Kind = e.Kind;
        Name = e.Name;
        GroupVcardUid = e.GroupVcardUid;
    }

    public void Apply(ContactGroupRenamed e) => Name = e.Name;

    public void Apply(ContactAddedToGroup e)
    {
        if (!MemberContactIds.Contains(e.ContactId)) MemberContactIds.Add(e.ContactId);
    }

    public void Apply(ContactRemovedFromGroup e) => MemberContactIds.Remove(e.ContactId);

    public void Apply(ContactGroupDeleted _) => DeletedAt = DateTimeOffset.UtcNow;
}
