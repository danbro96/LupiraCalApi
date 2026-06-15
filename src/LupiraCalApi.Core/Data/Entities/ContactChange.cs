namespace LupiraCalApi.Data.Entities;

/// <summary>Append-only change log + tombstones backing the CardDAV sync-collection REPORT.</summary>
public class ContactChange
{
    public Guid AddressBookId { get; set; }
    public long Revision { get; set; }
    public string ItemVcardUid { get; set; } = null!;
    public string ChangeType { get; set; } = null!;     // 'saved' | 'deleted'
    public string? ContentHash { get; set; }
}
