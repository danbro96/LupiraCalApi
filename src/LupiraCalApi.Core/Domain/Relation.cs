namespace LupiraCalApi.Domain;

/// <summary>
/// The single cross-API edge (plain document): a by-reference link from a calendar item or contact to something in
/// another service (e.g. a LupiraTasks item, or an Activity-API engagement/project). No FK — integrity is by convention.
/// </summary>
public sealed class Relation
{
    public Guid Id { get; set; }
    public string FromKind { get; set; } = "";   // "item" | "contact"
    public Guid FromId { get; set; }
    public string ToKind { get; set; } = "";      // e.g. "task" | "engagement" | "project" | "url"
    public string ToRef { get; set; } = "";
    public string RelationType { get; set; } = ""; // e.g. "related-to" | "derived-from" | "belongs-to"
    public string? Metadata { get; set; }          // free-form JSON
}
