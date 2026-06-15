namespace LupiraCalApi.Data.Entities;

/// <summary>Cross-domain edges, e.g. event → LupiraTasks item (by reference string, not FK — separate DBs).</summary>
public class Relation
{
    public Guid Id { get; set; }
    public string FromKind { get; set; } = null!;       // 'event' | 'contact'
    public Guid FromId { get; set; }
    public string ToKind { get; set; } = null!;         // 'task' | 'contact' | 'url'
    public string ToRef { get; set; } = null!;
    public string RelationType { get; set; } = null!;   // 'attendee' | 'related-to' | 'derived-from'
    public string? Metadata { get; set; }               // jsonb
}
