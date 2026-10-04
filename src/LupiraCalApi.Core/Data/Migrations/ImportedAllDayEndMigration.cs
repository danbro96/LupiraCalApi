using System.Text;
using LupiraCalApi.Core.Domain.CalendarItems.Events;
using Marten;
using Npgsql;
using NpgsqlTypes;

namespace LupiraCalApi.Core.Data.Migrations;

/// <summary>
/// One-shot, in-place conversion of the all-day items imported from phone sync while their <c>EndDate</c> was stored one
/// day past the last day: moves it back a day (never before <c>StartDate</c>), in one transaction. Rows written since the
/// fix carry <see cref="InclusiveEndHeader"/> and so does every converted row, so a re-run is a no-op. Take a backup, run
/// once, then <c>--rebuild-items</c>; this file is deleted afterwards.
/// </summary>
public static class ImportedAllDayEndMigration
{
    public const string InclusiveEndHeader = "all-day-end-inclusive";

    private const int SampleSize = 10;

    public static async Task<(int Count, IReadOnlyList<string> Samples)> RunAsync(
        IDocumentStore store, string connectionString, bool dryRun, CancellationToken ct)
    {
        var serializer = store.Options.Serializer();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var rewrites = new List<(long Seq, string Data)>();
        var samples = new List<string>();
        await using (var select = new NpgsqlCommand(
            "select seq_id, data::text from cal.mt_events where type = 'item_imported' and not coalesce(headers ? @header, false) order by seq_id",
            conn, tx))
        {
            select.Parameters.AddWithValue("header", InclusiveEndHeader);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                using var json = new MemoryStream(Encoding.UTF8.GetBytes(reader.GetString(1)));
                var e = serializer.FromJson<ItemImported>(json);
                if (e.Parsed is not { IsAllDay: true, StartDate: { } start, EndDate: { } end }) continue;
                var last = end.AddDays(-1) < start ? start : end.AddDays(-1);
                if (last == end) continue;
                rewrites.Add((reader.GetInt64(0), serializer.ToJson(e with { Parsed = e.Parsed with { EndDate = last } })));
                if (samples.Count < SampleSize) samples.Add($"{e.ItemId} \"{e.Parsed.Title}\" {start:yyyy-MM-dd}..{end:yyyy-MM-dd} -> {start:yyyy-MM-dd}..{last:yyyy-MM-dd}");
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
}
