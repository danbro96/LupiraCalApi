using System.Text;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.CalendarItems.Events;
using Marten;
using Npgsql;
using NpgsqlTypes;

namespace LupiraCalApi.Core.Data.Migrations;

/// <summary>
/// One-shot, in-place conversion of the occurrence changes on all-day series stored while their <c>EndsAt</c> was the
/// day after the last day: moves it back a day (never before the occurrence's start), in one transaction. Rows written
/// since the fix carry <see cref="InclusiveEndHeader"/> and so does every converted row, so a re-run is a no-op. Take a
/// backup, run once, then <c>--rebuild-items</c>; this file is deleted afterwards.
/// </summary>
public static class AllDayOccurrenceEndMigration
{
    public const string InclusiveEndHeader = "all-day-occurrence-end-inclusive";

    private const int SampleSize = 10;

    public static async Task<(int Count, IReadOnlyList<string> Samples)> RunAsync(
        IDocumentStore store, string connectionString, bool dryRun, CancellationToken ct)
    {
        var serializer = store.Options.Serializer();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var allDay = new Dictionary<Guid, bool>();
        var moved = new List<string>();
        var rewrites = new List<(long Seq, string Data)>();
        var samples = new List<string>();
        await using (var select = new NpgsqlCommand(
            "select seq_id, stream_id, type, data::text, coalesce(headers ? @header, false) from cal.mt_events " +
            "where type in ('item_scheduled', 'item_imported', 'item_revised', 'occurrence_overridden') order by seq_id",
            conn, tx))
        {
            select.Parameters.AddWithValue("header", InclusiveEndHeader);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var (seq, streamId, type, converted) = (reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2), reader.GetBoolean(4));
                moved.Clear();
                using var json = new MemoryStream(Encoding.UTF8.GetBytes(reader.GetString(3)));
                object? rewritten = type switch
                {
                    "item_scheduled" => Fields(serializer.FromJson<ItemScheduled>(json), e => e.Fields, (e, f) => e with { Fields = f }),
                    "item_imported" => Fields(serializer.FromJson<ItemImported>(json), e => e.Parsed, (e, f) => e with { Parsed = f }),
                    "item_revised" => Fields(serializer.FromJson<ItemRevised>(json), e => e.Fields, (e, f) => e with { Fields = f }),
                    _ => allDay.GetValueOrDefault(streamId) && serializer.FromJson<OccurrenceOverridden>(json) is var o && Inclusive(o.Override, moved) is { } c
                        ? o with { Override = c }
                        : null,
                };
                if (converted || rewritten is null) continue;
                rewrites.Add((seq, serializer.ToJson(rewritten)));
                if (samples.Count < SampleSize) samples.Add($"{streamId} {type} #{seq}: {string.Join(", ", moved)}");

                T? Fields<T>(T e, Func<T, CalendarItemFields> get, Func<T, CalendarItemFields, T> set)
                    where T : class
                {
                    var f = get(e);
                    allDay[streamId] = f.IsAllDay;
                    if (!f.IsAllDay || f.OccurrenceOverrides is not { } overrides) return null;
                    OccurrenceOverride[] inclusive = [.. overrides.Select(x => Inclusive(x, moved) ?? x)];
                    return inclusive.SequenceEqual(overrides) ? null : set(e, f with { OccurrenceOverrides = inclusive });
                }
            }
        }

        if (dryRun) return (rewrites.Count, samples);

        foreach (var (seq, data) in rewrites)
        {
            await using var update = new NpgsqlCommand(
                "update cal.mt_events set data = @data, headers = coalesce(headers, '{}'::jsonb) || jsonb_build_object(@header, true) where seq_id = @seq",
                conn, tx);
            update.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, data);
            update.Parameters.AddWithValue("header", InclusiveEndHeader);
            update.Parameters.AddWithValue("seq", seq);
            await update.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return (rewrites.Count, samples);
    }

    private static OccurrenceOverride? Inclusive(OccurrenceOverride o, List<string> moved)
    {
        if (o.EndsAt is not { } end) return null;
        var start = (o.StartsAt ?? o.OriginalStart).UtcDateTime.Date;
        var last = end.UtcDateTime.Date.AddDays(-1);
        var inclusive = new DateTimeOffset(last < start ? start : last, TimeSpan.Zero);
        if (inclusive == end) return null;
        moved.Add($"{o.OriginalStart:yyyy-MM-dd} ends {end:yyyy-MM-dd} -> {inclusive:yyyy-MM-dd}");
        return o with { EndsAt = inclusive };
    }
}
