using Npgsql;

namespace LupiraCalApi.Scheduling;

/// <summary>
/// The plain relational <c>cal.scheduled_fire</c> queue — operational state, NOT event-sourced (the same split as the raw
/// Npgsql tables in location/health-api). Created idempotently outside Marten's document differ; the materializer writes
/// rows via <c>IDocumentOperations.QueueSqlCommand</c> inside the daemon's checkpoint transaction.
/// </summary>
public static class ScheduledFireSchema
{
    public const string Table = "cal.scheduled_fire";

    public const string CreateDdl = """
        create schema if not exists cal;
        create table if not exists cal.scheduled_fire (
            id            uuid        primary key,
            item_id       uuid        not null,
            calendar_id   uuid        not null,
            occurrence_at timestamptz not null,
            prompt_ref    text        null,
            status        text        not null default 'pending',
            attempts      int         not null default 0,
            locked_until  timestamptz null,
            dedupe_key    text        not null,
            expire_after  interval    null,
            last_error    text        null,
            fired_at      timestamptz null,
            constraint scheduled_fire_dedupe_key_unique unique (dedupe_key)
        );
        create index if not exists scheduled_fire_status_occurrence_idx on cal.scheduled_fire (status, occurrence_at);
        """;

    /// <summary>Idempotent upsert (one row per occurrence across retries/restarts). Positional <c>?</c> placeholders for QueueSqlCommand.</summary>
    public const string InsertSql = """
        insert into cal.scheduled_fire (id, item_id, calendar_id, occurrence_at, prompt_ref, status, attempts, expire_after, dedupe_key)
        values (?, ?, ?, ?, ?, 'pending', 0, ?, ?)
        on conflict (dedupe_key) do nothing
        """;

    /// <summary>Drop an item's not-yet-due, unclaimed fires (on re-materialize, clear, delete, cancel). Leaves claimed/done/failed/expired for audit.</summary>
    public const string DeleteFuturePendingSql =
        "delete from cal.scheduled_fire where item_id = ? and status = 'pending' and occurrence_at > now()";

    public static async Task EnsureExistsAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(CreateDdl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
