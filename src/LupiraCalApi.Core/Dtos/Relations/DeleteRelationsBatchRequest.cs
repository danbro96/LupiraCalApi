namespace LupiraCalApi.Core.Dtos.Relations;

/// <summary>Many references of one kind unlinked from one item in a single call (e.g. photos removed from an event's album).</summary>
public sealed class DeleteRelationsBatchRequest
{
    public required string ToKind { get; set; }

    public required string RelationType { get; set; }

    public required List<string> ToRefs { get; set; }
}
