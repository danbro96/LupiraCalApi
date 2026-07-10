using LupiraCalApi.Domain;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Pure kinship derivation over an in-memory contact set — parent/child edges read from either storage side,
/// two-generation closure, explicit-edge precedence.</summary>
public class KinshipInferenceTests
{
    // A three-generation family with parentage stored on mixed sides:
    //   A --Parent--> P,  P --Child--> B,  P --Parent--> G,  G --Child--> U,  U --Child--> C
    // So: G is grandparent of A/B; P & U are G's children (siblings); A & B are P's children; C is U's child.
    static readonly Guid G = new("11111111-1111-1111-1111-111111111111");
    static readonly Guid P = new("22222222-2222-2222-2222-222222222222");
    static readonly Guid U = new("33333333-3333-3333-3333-333333333333");
    static readonly Guid A = new("44444444-4444-4444-4444-444444444444");
    static readonly Guid B = new("55555555-5555-5555-5555-555555555555");
    static readonly Guid C = new("66666666-6666-6666-6666-666666666666");

    static Contact Person(Guid id, params (Guid to, ContactRelationKind kind)[] rels) =>
        new() { Id = id, Relations = [.. rels.Select(r => new ContactRelation { ToContactId = r.to, Kind = r.kind })] };

    static List<Contact> Family() =>
    [
        Person(G, (U, ContactRelationKind.Child)),
        Person(P, (G, ContactRelationKind.Parent), (B, ContactRelationKind.Child)),
        Person(U, (C, ContactRelationKind.Child)),
        Person(A, (P, ContactRelationKind.Parent)),
        Person(B),
        Person(C),
    ];

    static Dictionary<Guid, KinshipKind> Infer(Guid focus, IReadOnlyCollection<Contact> contacts) =>
        KinshipInference.Infer(focus, contacts).ToDictionary(k => k.ContactId, k => k.Kind);

    [Fact]
    public void Infers_the_two_generation_closure_around_a_child()
    {
        var kin = Infer(A, Family());
        Assert.Equal(KinshipKind.Sibling, kin[B]);
        Assert.Equal(KinshipKind.Grandparent, kin[G]);
        Assert.Equal(KinshipKind.AuntUncle, kin[U]);
        Assert.Equal(KinshipKind.Cousin, kin[C]);
        Assert.False(kin.ContainsKey(P));   // P is an explicit parent, surfaced separately
        Assert.False(kin.ContainsKey(A));
    }

    [Fact]
    public void Infers_grandchildren_from_the_top()
    {
        var kin = Infer(G, Family());
        Assert.Equal(KinshipKind.Grandchild, kin[A]);
        Assert.Equal(KinshipKind.Grandchild, kin[B]);
        Assert.Equal(KinshipKind.Grandchild, kin[C]);
        Assert.False(kin.ContainsKey(P));   // explicit child (incoming Parent edge)
        Assert.False(kin.ContainsKey(U));   // explicit child (outgoing Child edge)
    }

    [Fact]
    public void Infers_nieces_and_nephews_for_an_uncle()
    {
        var kin = Infer(U, Family());
        Assert.Equal(KinshipKind.Sibling, kin[P]);       // co-child of G
        Assert.Equal(KinshipKind.NieceNephew, kin[A]);   // child of sibling P
        Assert.Equal(KinshipKind.NieceNephew, kin[B]);
    }

    [Fact]
    public void Derives_siblings_from_a_shared_parent_regardless_of_storage_side()
    {
        // X stores its parent; Y's parentage is stored on the parent as a Child edge. Still siblings.
        var parent = Guid.NewGuid();
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        var contacts = new List<Contact>
        {
            Person(x, (parent, ContactRelationKind.Parent)),
            Person(parent, (y, ContactRelationKind.Child)),
            Person(y),
        };
        Assert.Equal(KinshipKind.Sibling, Infer(x, contacts)[y]);
        Assert.Equal(KinshipKind.Sibling, Infer(y, contacts)[x]);
    }

    [Fact]
    public void Explicit_edges_win_over_inferred_kinship()
    {
        var family = Family();
        // Pin an explicit Friend edge A→C; C must not also surface as an inferred cousin.
        family.Single(c => c.Id == A).Relations.Add(new ContactRelation { ToContactId = C, Kind = ContactRelationKind.Friend });
        Assert.False(Infer(A, family).ContainsKey(C));
    }

    [Fact]
    public void Normalize_returns_parents_and_explicit_siblings()
    {
        var p1 = Guid.NewGuid();
        var s1 = Guid.NewGuid();
        var focus = Guid.NewGuid();
        var contacts = new List<Contact>
        {
            Person(focus, (p1, ContactRelationKind.Parent), (s1, ContactRelationKind.Sibling)),
            Person(p1),
            Person(s1),
        };
        var (parents, siblings) = KinshipInference.Normalize(focus, contacts);
        Assert.Equal([p1], parents);
        Assert.Equal([s1], siblings);
    }
}
