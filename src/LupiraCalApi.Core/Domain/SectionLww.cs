using JasperFx.Events;

namespace LupiraCalApi.Domain;

/// <summary>
/// Per-section last-writer-wins rules for <see cref="CalendarItem"/>. A "section" is the slice of the item one
/// write endpoint owns (core fields, metadata, the XOR payload, per-calendar filing); each keeps an
/// (occurredAt, commandId) guard on the snapshot and an incoming event mutates its section only when strictly
/// newer. Ported from LupiraTasksApi's ItemLww — the mobile client reducer must match byte-for-byte.
/// </summary>
public static class SectionLww
{
    /// <summary>True when an incoming event keyed (<paramref name="occurredAt"/>, <paramref name="commandId"/>) is
    /// strictly newer than a section's guard: later occurredAt, or equal occurredAt with a greater commandId.
    /// An equal pair (a replay) loses, so apply is idempotent.</summary>
    public static bool Wins(DateTimeOffset occurredAt, Guid commandId, DateTimeOffset guardTs, Guid guardCmd) =>
        occurredAt > guardTs || (occurredAt == guardTs && CompareCommandId(commandId, guardCmd) > 0);

    /// <summary>Tiebreaker for an exact occurredAt tie: ordinal comparison of the canonical lowercase GUID strings.
    /// Deliberately NOT <see cref="Guid.CompareTo"/>, whose byte-group ordering a JS reducer can't reproduce.</summary>
    public static int CompareCommandId(Guid a, Guid b) =>
        string.CompareOrdinal(a.ToString(), b.ToString());

    /// <summary>Effective (occurredAt, commandId) for an event: the client's stamp when present, else the
    /// server-recorded event timestamp plus a command id encoding the global sequence as zero-padded hex — its
    /// canonical string order equals numeric sequence order, so unstamped events (web, DAV, pre-existing history)
    /// always apply in append order and rebuilds stay deterministic.</summary>
    public static (DateTimeOffset Ts, Guid Cmd) Stamp<T>(IEvent<T> e, DateTimeOffset? occurredAt, Guid? commandId) where T : class =>
        (occurredAt ?? e.Timestamp, commandId ?? FromSequence(e.Sequence));

    public static Guid FromSequence(long sequence) => new(sequence.ToString("x32"));
}
