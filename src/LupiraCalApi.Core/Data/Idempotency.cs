using JasperFx;
using LupiraCalApi.Domain;
using Marten;

namespace LupiraCalApi.Data;

/// <summary>
/// Offline-first idempotency gate (mirrors LupiraTasksApi's). A mutation may carry an <c>Idempotency-Key</c>
/// header (a client-minted GUIDv7 command id); a mobile outbox resends the same key after a lost response, so a
/// redelivered command must be a no-op returning the prior result.
/// <para>The dedup row and the event append share ONE <see cref="IDocumentSession"/> and ONE
/// <c>SaveChangesAsync</c>. The row goes in via <see cref="IDocumentSession.Insert{T}"/> — a plain INSERT, not an
/// upsert — so a concurrent duplicate violates the <see cref="ProcessedCommand"/> primary key and rolls back the
/// whole transaction including the loser's staged events, which the loser treats as idempotent success. Closes
/// the check-then-write TOCTOU an upsert would leave open.</para>
/// <para>Without a key the append simply commits — no cross-request dedup. Creates need none: <c>SourceKey</c>
/// already pins the stream id, making replayed creates idempotent.</para>
/// </summary>
public sealed class Idempotency(IDocumentSession session)
{
    /// <summary>The <see cref="ProcessedCommand"/> already recorded for <paramref name="commandId"/>, or null when
    /// the command is new (or no key was supplied). Callers return the existing aggregate on a hit.</summary>
    public async Task<ProcessedCommand?> SeenAsync(Guid? commandId, CancellationToken ct) =>
        commandId is { } key ? await session.LoadAsync<ProcessedCommand>(key, ct) : null;

    /// <summary>Stage the ledger row alongside already-staged events; the caller owns the single
    /// <c>SaveChangesAsync</c> so it can catch the duplicate-key rollback via <see cref="IsDuplicate"/>.</summary>
    public void Record(Guid? commandId, Guid aggregateId, int resultVersion)
    {
        if (commandId is { } id)
        {
            session.Insert(new ProcessedCommand
            {
                CommandId = id,
                AggregateId = aggregateId,
                ResultVersion = resultVersion,
                ProcessedAt = DateTimeOffset.UtcNow,
            });
        }
    }

    /// <summary>True when a <c>SaveChangesAsync</c> failure is the dedup race being lost (another request with the
    /// same key committed first) — the caller should re-read and return the existing aggregate.</summary>
    public static bool IsDuplicate(Exception ex) => ex is DocumentAlreadyExistsException;
}
