namespace LupiraCalApi.Core.Dtos.Relations;

/// <summary>Many references of one kind linked to one item in a single call (e.g. an album's photos to its event).</summary>
public sealed class CreateRelationsBatchRequest
{
    public required string ToKind { get; set; }

    public required string RelationType { get; set; }

    public required List<string> ToRefs { get; set; }
}
