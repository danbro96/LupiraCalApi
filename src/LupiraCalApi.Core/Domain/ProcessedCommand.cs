namespace LupiraCalApi.Domain;

/// <summary>Idempotency ledger row: marks a command (the client's <c>Idempotency-Key</c>) as already processed so
/// a redelivered mutation is a no-op returning the prior result instead of a duplicate write.</summary>
public sealed class ProcessedCommand
{
    /// <summary>Marten document identity — the originating command id (client-minted UUIDv7).</summary>
    public Guid CommandId { get; set; }

    public Guid AggregateId { get; set; }

    public int ResultVersion { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}
