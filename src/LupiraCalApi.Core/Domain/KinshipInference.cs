namespace LupiraCalApi.Domain;

/// <summary>A kinship derived from the parent/child graph (never stored). Pure over a supplied set of contacts,
/// like <see cref="CompletenessScorer"/>; the session-bound loading + access filtering lives in ContactService.</summary>
public readonly record struct InferredKin(Guid ContactId, KinshipKind Kind);

/// <summary>
/// Derives family relationships from stored <c>Parent</c>/<c>Child</c>/<c>Sibling</c> edges (read from either storage
/// side). Bounded to a two-generation closure: siblings, grandparents/grandchildren, aunts-uncles, nieces-nephews, cousins.
/// </summary>
public static class KinshipInference
{
    /// <summary>Inferred kin of <paramref name="focusId"/>, one role per contact (closest wins), excluding the focus and
    /// anyone it already relates to explicitly (explicit edges win — the caller surfaces those separately).</summary>
    public static IReadOnlyList<InferredKin> Infer(Guid focusId, IReadOnlyCollection<Contact> contacts)
    {
        var g = new Graph(contacts);
        if (!g.Known.Contains(focusId)) return [];

        var explicitPartners = g.ExplicitPartners(focusId);
        var parents = g.Parents(focusId);
        var children = g.Children(focusId);

        // Kinds in precedence order; first assignment for a contact wins.
        var buckets = new (KinshipKind Kind, IEnumerable<Guid> Ids)[]
        {
            (KinshipKind.Sibling, g.Siblings(focusId)),
            (KinshipKind.Grandparent, parents.SelectMany(g.Parents)),
            (KinshipKind.Grandchild, children.SelectMany(g.Children)),
            (KinshipKind.AuntUncle, parents.SelectMany(g.Siblings).Except(parents)),
            (KinshipKind.NieceNephew, g.Siblings(focusId).SelectMany(g.Children)),
            (KinshipKind.Cousin, parents.SelectMany(g.Siblings).Except(parents).SelectMany(g.Children)),
        };

        var seen = new HashSet<Guid> { focusId };
        seen.UnionWith(explicitPartners);
        var result = new List<InferredKin>();
        foreach (var (kind, ids) in buckets)
            foreach (var id in ids)
                if (g.Known.Contains(id) && seen.Add(id))
                    result.Add(new InferredKin(id, kind));
        return result;
    }

    /// <summary>The focus's parent ids and explicit-sibling ids — the inputs the sibling invariant needs.</summary>
    public static (IReadOnlyCollection<Guid> Parents, IReadOnlyCollection<Guid> ExplicitSiblings) Normalize(
        Guid focusId, IReadOnlyCollection<Contact> contacts)
    {
        var g = new Graph(contacts);
        return (g.Parents(focusId), g.ExplicitSiblings(focusId));
    }

    // Undirected adjacency over parent/child/sibling edges, reading each edge from whichever side stored it.
    private sealed class Graph
    {
        public readonly HashSet<Guid> Known;
        private readonly Dictionary<Guid, HashSet<Guid>> _parents = new();   // x -> parents of x
        private readonly Dictionary<Guid, HashSet<Guid>> _children = new();  // x -> children of x
        private readonly Dictionary<Guid, HashSet<Guid>> _siblings = new();  // x -> explicit siblings of x
        private readonly Dictionary<Guid, HashSet<Guid>> _partners = new();  // x -> explicit edge partners (any kind)

        public Graph(IReadOnlyCollection<Contact> contacts)
        {
            Known = [.. contacts.Select(c => c.Id)];
            foreach (var c in contacts)
                foreach (var r in c.Relations)
                {
                    Link(_partners, c.Id, r.ToContactId);
                    switch (r.Kind)
                    {
                        case ContactRelationKind.Parent: Link(_parents, c.Id, r.ToContactId); Link(_children, r.ToContactId, c.Id); break;
                        case ContactRelationKind.Child: Link(_children, c.Id, r.ToContactId); Link(_parents, r.ToContactId, c.Id); break;
                        case ContactRelationKind.Sibling: Link(_siblings, c.Id, r.ToContactId); Link(_siblings, r.ToContactId, c.Id); break;
                    }
                }
        }

        public IReadOnlyCollection<Guid> Parents(Guid x) => Get(_parents, x);
        public IReadOnlyCollection<Guid> Children(Guid x) => Get(_children, x);
        public IReadOnlyCollection<Guid> ExplicitSiblings(Guid x) => Get(_siblings, x);

        // Co-children of x's parents (minus x) plus any explicit sibling edges.
        public IReadOnlyCollection<Guid> Siblings(Guid x)
        {
            var s = new HashSet<Guid>(Get(_siblings, x));
            foreach (var p in Get(_parents, x)) s.UnionWith(Get(_children, p));
            s.Remove(x);
            return s;
        }

        public IReadOnlyCollection<Guid> ExplicitPartners(Guid x)
        {
            var s = new HashSet<Guid>(Get(_partners, x));
            foreach (var c in _partners) if (c.Value.Contains(x)) s.Add(c.Key);   // partners stored on the other side
            return s;
        }

        private static void Link(Dictionary<Guid, HashSet<Guid>> map, Guid from, Guid to)
        {
            if (!map.TryGetValue(from, out var set)) map[from] = set = [];
            set.Add(to);
        }

        private static IReadOnlyCollection<Guid> Get(Dictionary<Guid, HashSet<Guid>> map, Guid x) =>
            map.TryGetValue(x, out var set) ? set : [];
    }
}
