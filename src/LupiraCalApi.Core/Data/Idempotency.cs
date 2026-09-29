using JasperFx;
using LupiraCalApi.Core.Domain.Shared;
using Marten;

namespace LupiraCalApi.Core.Data;

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

    /// <summary>Commit the staged events together with the ledger row for <paramref name="commandId"/>, in one
    /// transaction. False when the dedup race was lost — another request with the same key committed first — and the
    /// caller returns the already-committed state (idempotent success).</summary>
    public async Task<bool> CommitAsync(Guid? commandId, Guid aggregateId, int resultVersion, CancellationToken ct)
    {
        Record(commandId, aggregateId, resultVersion);
        try
        {
            await session.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsDuplicate(ex))
        {
            return false;
        }

        return true;
    }

    private void Record(Guid? commandId, Guid aggregateId, int resultVersion)
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

    private static bool IsDuplicate(Exception ex) => ex is DocumentAlreadyExistsException;
}
