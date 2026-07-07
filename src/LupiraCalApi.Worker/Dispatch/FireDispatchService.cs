using LupiraCalApi.Domain;
using LupiraCalApi.Scheduling;
using LupiraCalApi.Worker.Clients;
using LupiraCalApi.Worker.Dtos;
using Marten;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LupiraCalApi.Worker.Dispatch;

/// <summary>
/// One dispatcher tick: expire over-age rows, claim a due batch, deliver each fire. The row is only the clock —
/// the item aggregate is the payload's source of truth at dispatch time (it may have been cleared/disabled since
/// materialization; only *future* pending rows are dropped by the projection).
/// </summary>
public sealed class FireDispatchService(
    NpgsqlDataSource db,
    IQuerySession session,
    AssistantFireClient assistant,
    IOptions<DispatcherOptions> options,
    ILogger<FireDispatchService> logger)
{
    private readonly DispatcherOptions _opts = options.Value;

    public async Task<int> RunTickAsync(CancellationToken ct)
    {
        await ExpireOverdueAsync(ct);
        var claimed = await ClaimAsync(ct);
        foreach (var fire in claimed)
        {
            try
            {
                await DispatchOneAsync(fire, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad row never poisons the batch; its lease lapses and it is retried.
                logger.LogError(ex, "Dispatch failed for fire {FireId} (item {ItemId}).", fire.Id, fire.ItemId);
            }
        }
        return claimed.Count;
    }

    private async Task ExpireOverdueAsync(CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(ScheduledFireDispatchSql.ExpireSql);
        var expired = await cmd.ExecuteNonQueryAsync(ct);
        if (expired > 0) logger.LogWarning("Expired {Count} undelivered fire(s) past their expire_after.", expired);
    }

    private async Task<IReadOnlyList<ClaimedFire>> ClaimAsync(CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(ScheduledFireDispatchSql.ClaimSql);
        cmd.Parameters.AddWithValue("batch", _opts.BatchSize);
        cmd.Parameters.AddWithValue("lease", _opts.Lease);

        var rows = new List<ClaimedFire>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new ClaimedFire(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt32(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<TimeSpan>(8)));
        return rows;
    }

    private async Task DispatchOneAsync(ClaimedFire fire, CancellationToken ct)
    {
        var item = await session.LoadAsync<CalendarItem>(fire.ItemId, ct);
        if (item is null || item.DeletedAt is not null || item.Status == ItemStatus.Cancelled)
        {
            await TransitionAsync(ScheduledFireDispatchSql.ExpireOneSql, fire.Id, error: "item deleted/cancelled", ct: ct);
            return;
        }

        var prompt = item.Prompt is { Enabled: true } p ? p : null;
        var action = prompt is null && item.Action is { Enabled: true } a ? a : null;
        if (prompt is null && action is null)
        {
            await TransitionAsync(ScheduledFireDispatchSql.ExpireOneSql, fire.Id, error: "payload cleared or disabled", ct: ct);
            return;
        }

        if (fire.CalendarId == Guid.Empty)
        {
            await TransitionAsync(ScheduledFireDispatchSql.FailSql, fire.Id, error: "no calendar membership at materialisation", ct: ct);
            return;
        }
        var calendar = await session.LoadAsync<Calendar>(fire.CalendarId, ct);
        if (calendar is null)
        {
            await TransitionAsync(ScheduledFireDispatchSql.FailSql, fire.Id, error: "calendar not found", ct: ct);
            return;
        }

        var email = await ResolvePrincipalEmailAsync(fire, ct);
        if (string.IsNullOrWhiteSpace(email))
        {
            await TransitionAsync(ScheduledFireDispatchSql.FailSql, fire.Id, error: "no resolvable owner principal", ct: ct);
            return;
        }

        var request = new FireRequest
        {
            PrincipalId = email,
            ItemId = fire.ItemId,
            CalendarId = fire.CalendarId,
            CalendarClass = calendar.Class,
            CalendarKind = calendar.Kind,
            OccurrenceAt = fire.OccurrenceAt,
            DedupeKey = fire.DedupeKey,
            ExpireAfter = fire.ExpireAfter is { } after ? fire.OccurrenceAt + after : null,
            Prompt = prompt,
            Action = action,
        };

        var result = await assistant.PostFireAsync(request, ct);
        if (result.Accepted)
        {
            await TransitionAsync(ScheduledFireDispatchSql.DoneSql, fire.Id, ct: ct);
            logger.LogInformation("Fire {FireId} delivered (item {ItemId}, occurrence {OccurrenceAt:O}{Duplicate}).",
                fire.Id, fire.ItemId, fire.OccurrenceAt, result.Duplicate ? ", duplicate" : "");
        }
        else if (!result.Retryable || fire.Attempts >= _opts.MaxAttempts)
        {
            await TransitionAsync(ScheduledFireDispatchSql.FailSql, fire.Id, error: result.Error, ct: ct);
            logger.LogError("Fire {FireId} failed after {Attempts} attempt(s): {Error}", fire.Id, fire.Attempts, result.Error);
        }
        else
        {
            var backoff = DispatchBackoff.Delay(fire.Attempts);
            await TransitionAsync(ScheduledFireDispatchSql.RetrySql, fire.Id, error: result.Error, backoff: backoff, ct: ct);
            logger.LogWarning("Fire {FireId} push failed (attempt {Attempts}), retrying in {Backoff}: {Error}",
                fire.Id, fire.Attempts, backoff, result.Error);
        }
    }

    /// <summary>The stamped principal, or (legacy rows materialized before principal stamping) the fire calendar's
    /// first Owner grant — resolved to the mutable email join key at fire time.</summary>
    private async Task<string?> ResolvePrincipalEmailAsync(ClaimedFire fire, CancellationToken ct)
    {
        var principalId = fire.PrincipalId;
        if (principalId is null)
        {
            var owners = await session.Query<CalendarOwner>()
                .Where(o => o.CalendarId == fire.CalendarId && o.Access == Access.Owner)
                .ToListAsync(ct);
            principalId = owners.OrderBy(o => o.PrincipalId).FirstOrDefault()?.PrincipalId;
        }
        if (principalId is null) return null;

        var principal = await session.LoadAsync<Principal>(principalId.Value, ct);
        return principal?.Email;
    }

    private async Task TransitionAsync(string sql, Guid id, string? error = null, TimeSpan? backoff = null, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", id);
        if (sql.Contains("@error")) cmd.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
        if (sql.Contains("@backoff")) cmd.Parameters.AddWithValue("backoff", backoff ?? TimeSpan.Zero);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
