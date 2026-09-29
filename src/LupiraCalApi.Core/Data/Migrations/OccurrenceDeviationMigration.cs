using System.Text;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.CalendarItems.Events;
using LupiraCalApi.Core.Serialization;
using Marten;
using Npgsql;
using NpgsqlTypes;

namespace LupiraCalApi.Core.Data.Migrations;

/// <summary>
/// One-shot, in-place conversion of the item events stored while a series' per-occurrence deviations were raw text (the
/// master's EXDATE/RDATE lines and override VEVENTs, as DAV sent them) into the structured fields. Rewrites only rows that
/// still carry the old keys, in one transaction, so a re-run is a no-op. Take a backup, run once, then
/// <c>--rebuild-items</c>; this folder is deleted afterwards.
/// </summary>
public static class OccurrenceDeviationMigration
{
    private static readonly string[] EventTypes = ["item_scheduled", "item_imported", "item_revised"];

    public static async Task<int> RunAsync(IDocumentStore store, string connectionString, CancellationToken ct)
    {
        var serializer = store.Options.Serializer();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var rewrites = new List<(long Seq, string Data)>();
        await using (var select = new NpgsqlCommand("select seq_id, type, data::text from cal.mt_events where type = any(@types) order by seq_id", conn, tx))
        {
            select.Parameters.AddWithValue("types", EventTypes);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var data = reader.GetString(2);
                if (!HasOldKeys(data)) continue;
                rewrites.Add((reader.GetInt64(0), serializer.ToJson(Convert(reader.GetString(1), data, serializer))));
            }
        }

        foreach (var (seq, data) in rewrites)
        {
            await using var update = new NpgsqlCommand("update cal.mt_events set data = @data where seq_id = @seq", conn, tx);
            update.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, data);
            update.Parameters.AddWithValue("seq", seq);
            await update.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return rewrites.Count;
    }

    private static bool HasOldKeys(string data) =>
        data.Contains("RecurrenceExceptions", StringComparison.OrdinalIgnoreCase) || data.Contains("RecurrenceOverrides", StringComparison.OrdinalIgnoreCase);

    private static object Convert(string type, string data, ISerializer serializer)
    {
        using var json = new MemoryStream(Encoding.UTF8.GetBytes(data));
        switch (type)
        {
            case "item_scheduled":
                var s = serializer.FromJson<ItemScheduledV1>(json);
                return new ItemScheduled(s.ItemId, s.ExternalId, Convert(s.Fields), s.Details);
            case "item_imported":
                var i = serializer.FromJson<ItemImportedV1>(json);
                return new ItemImported(i.ItemId, i.ExternalId, Convert(i.Parsed));
            default:
                var r = serializer.FromJson<ItemRevisedV1>(json);
                return new ItemRevised(r.ItemId, Convert(r.Fields), r.Details, r.OccurredAt, r.CommandId);
        }
    }

    public static CalendarItemFields Convert(CalendarItemFieldsV1 f)
    {
        var (excluded, extra, overrides) = ParseDeviationText(f);
        return new CalendarItemFields(f.Title, f.Description, f.Status, f.IsAllDay, f.StartsAt, f.EndsAt, f.StartTimezone, f.EndTimezone,
            f.StartDate, f.EndDate, f.RecurrenceRule, excluded, extra, overrides, f.Category, f.PlaceId, f.LocationLabel, f.ParentItemId,
            f.Tags, f.StartPrecision, f.EndPrecision);
    }

    // Reads the text exactly as a DAV PUT carrying it would be read: spliced back into the regenerated series.
    private static (DateTimeOffset[]?, DateTimeOffset[]?, OccurrenceOverride[]?) ParseDeviationText(CalendarItemFieldsV1 f)
    {
        if (string.IsNullOrWhiteSpace(f.RecurrenceExceptions) && string.IsNullOrWhiteSpace(f.RecurrenceOverrides)) return (null, null, null);

        var ics = ICalSerializer.ToICalendar("migration", f.Title, f.Description, f.LocationLabel, null, f.IsAllDay, f.StartsAt, f.EndsAt,
            f.StartDate, f.EndDate, f.RecurrenceRule);
        if (!string.IsNullOrWhiteSpace(f.RecurrenceExceptions))
            ics = ics.Insert(ics.IndexOf("END:VEVENT", StringComparison.Ordinal), Crlf(f.RecurrenceExceptions) + "\r\n");
        if (!string.IsNullOrWhiteSpace(f.RecurrenceOverrides))
            ics = ics.Insert(ics.LastIndexOf("END:VCALENDAR", StringComparison.Ordinal), Crlf(f.RecurrenceOverrides) + "\r\n");

        var p = ICalSerializer.ParseICalendar(ics);
        return (p.ExcludedOccurrences, p.ExtraOccurrences, p.OccurrenceOverrides);
    }

    private static string Crlf(string s) => s.Replace("\r\n", "\n").Replace("\n", "\r\n").TrimEnd('\r', '\n');
}
