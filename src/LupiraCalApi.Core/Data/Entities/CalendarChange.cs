namespace LupiraCalApi.Data.Entities;

/// <summary>Append-only change log + tombstones backing the CalDAV sync-collection REPORT.</summary>
public class CalendarChange
{
    public Guid CalendarId { get; set; }
    public long Revision { get; set; }
    public string ItemIcalUid { get; set; } = null!;
    public string ChangeType { get; set; } = null!;     // 'saved' | 'deleted'
    public string? ContentHash { get; set; }            // null for deletes
}
