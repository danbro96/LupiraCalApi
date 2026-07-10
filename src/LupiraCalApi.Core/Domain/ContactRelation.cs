namespace LupiraCalApi.Domain;

/// <summary>A typed, directed relation edge embedded in the owning contact's snapshot: "the To contact is my Kind"
/// (vCard <c>RELATED</c> semantics). Keyed by (ToContactId, Kind); <c>Label</c> is a free-text refinement ("dad").
/// No FK — the target may be deleted or unreadable; resolved read surfaces filter.</summary>
public sealed class ContactRelation
{
    public Guid ToContactId { get; set; }
    public ContactRelationKind Kind { get; set; }
    public string? Label { get; set; }
}

/// <summary>Derives the kind seen from the other side of an edge (the incoming view).</summary>
public static class ContactRelationKinds
{
    public static ContactRelationKind Inverse(this ContactRelationKind kind) => kind switch
    {
        ContactRelationKind.Parent => ContactRelationKind.Child,
        ContactRelationKind.Child => ContactRelationKind.Parent,
        ContactRelationKind.Emergency => ContactRelationKind.Other,   // no true inverse
        _ => kind,   // remaining kinds are symmetric
    };

    /// <summary>Widen a stored kind to the read-model <see cref="KinshipKind"/>; the two enums share leading ordinals.</summary>
    public static KinshipKind AsKinship(this ContactRelationKind kind) => (KinshipKind)(int)kind;
}
