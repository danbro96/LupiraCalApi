namespace LupiraCalApi.Scheduling;

/// <summary>
/// The dispatcher's state transitions over <c>cal.scheduled_fire</c> — named-parameter SQL the worker executes via
/// Npgsql. All time comparisons use the DB clock (<c>now()</c>); the worker never compares wall-clocks. Claimed rows
/// whose lease lapsed are re-claimable, which is the whole crash-recovery story.
/// </summary>
public static class ScheduledFireDispatchSql
{
    /// <summary>Atomically claim a due batch: lease it and count the attempt. Ordered oldest-first; SKIP LOCKED keeps
    /// concurrent workers from double-claiming within a statement, the lease keeps them apart across statements.</summary>
    public const string ClaimSql = """
        with due as (
            select id from cal.scheduled_fire
            where status in ('pending','claimed')
              and occurrence_at <= now()
              and (locked_until is null or locked_until <= now())
            order by occurrence_at
            limit @batch
            for update skip locked)
        update cal.scheduled_fire f
        set status = 'claimed', attempts = f.attempts + 1, locked_until = now() + @lease
        from due where f.id = due.id
        returning f.id, f.item_id, f.calendar_id, f.principal_id, f.occurrence_at,
                  f.prompt_ref, f.attempts, f.dedupe_key, f.expire_after
        """;

    /// <summary>Run first each tick. The lease guard means a row mid-delivery is never expired under the worker.</summary>
    public const string ExpireSql = """
        update cal.scheduled_fire
        set status = 'expired', last_error = coalesce(last_error, 'expired before delivery')
        where status in ('pending','claimed')
          and (locked_until is null or locked_until <= now())
          and occurrence_at + coalesce(expire_after, interval '24 hours') < now()
        """;

    public const string DoneSql =
        "update cal.scheduled_fire set status = 'done', fired_at = now(), locked_until = null, last_error = null where id = @id";

    /// <summary>Transient push failure with attempts left: back to pending, backoff carried by the lease column.</summary>
    public const string RetrySql =
        "update cal.scheduled_fire set status = 'pending', locked_until = now() + @backoff, last_error = @error where id = @id";

    /// <summary>Attempts exhausted, or a non-retryable condition (400, unresolvable calendar/principal).</summary>
    public const string FailSql =
        "update cal.scheduled_fire set status = 'failed', locked_until = null, last_error = @error where id = @id";

    /// <summary>The backing item/payload is gone — the fire will never be delivered ('done' would lie, 'failed' would alarm).</summary>
    public const string ExpireOneSql =
        "update cal.scheduled_fire set status = 'expired', locked_until = null, last_error = @error where id = @id";
}
