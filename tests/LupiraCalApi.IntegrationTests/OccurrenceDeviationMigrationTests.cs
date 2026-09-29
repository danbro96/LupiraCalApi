using JasperFx;
using JasperFx.Events;
using LupiraCalApi.Core.Data.Migrations;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.CalendarItems.Events;
using Marten;
using Npgsql;
using Weasel.Core;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>The one-shot in-place conversion against Postgres: rows written in the old shape are rewritten to the structured
/// one (under the same event names), rebuild into structured deviations, and a re-run touches nothing.</summary>
public sealed class OccurrenceDeviationMigrationTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private static readonly DateTimeOffset Start = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private const string OverrideText =
        "BEGIN:VEVENT\r\nUID:legacy@x\r\nRECURRENCE-ID:20260702T090000Z\r\nDTSTART:20260702T140000Z\r\nDTEND:20260702T150000Z\r\nSUMMARY:Standup\r\nEND:VEVENT";

    [Fact]
    public async Task Old_shape_rows_convert_in_place_and_rebuild_structured()
    {
        var withText = Guid.NewGuid();
        var plain = Guid.NewGuid();
        CalendarItemFieldsV1 Fields(string? exceptions, string? overrides) => new("Standup", null, null, false, Start, Start.AddHours(1), "UTC",
            null, null, null, "FREQ=DAILY", exceptions, overrides, null, null, null, null, null);

        // Writes exactly what the store held before the change: the old payloads under the same event names.
        using (var old = DocumentStore.For(o =>
        {
            o.Connection(Factory.ConnectionString);
            o.DatabaseSchemaName = "cal";
            o.UseSystemTextJsonForSerialization(EnumStorage.AsString);
            o.AutoCreateSchemaObjects = AutoCreate.None;
            o.Events.AppendMode = EventAppendMode.Rich;
            o.Events.MapEventType<ItemImportedV1>("item_imported");
            o.Events.MapEventType<ItemRevisedV1>("item_revised");
        }))
        {
            await using var session = old.LightweightSession();
            session.Events.StartStream<CalendarItem>(withText, new ItemImportedV1(withText, "legacy@x", Fields("EXDATE:20260703T090000Z", OverrideText)));
            session.Events.StartStream<CalendarItem>(plain, new ItemImportedV1(plain, "plain@x", Fields(null, null)),
                new ItemRevisedV1(plain, Fields(null, null) with { Title = "Renamed" }, null));
            await session.SaveChangesAsync();
        }

        var converted = await OccurrenceDeviationMigration.RunAsync(Store, Factory.ConnectionString, CancellationToken.None);

        Assert.Equal(await CountRowsAsync("true"), converted);   // every old-shape row, not only those with text
        Assert.Equal(0, await CountRowsAsync("data::text ilike '%RecurrenceExceptions%' or data::text ilike '%RecurrenceOverrides%'"));
        Assert.Equal(0, await OccurrenceDeviationMigration.RunAsync(Store, Factory.ConnectionString, CancellationToken.None));

        using var daemon = await Store.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<CalendarItem>(CancellationToken.None);

        await using var query = Store.QuerySession();
        Assert.IsType<ItemImported>(Assert.Single(await query.Events.FetchStreamAsync(withText)).Data);
        var item = await query.LoadAsync<CalendarItem>(withText);
        Assert.Equal([new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero)], item!.ExcludedOccurrences!);
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 14, 0, 0, TimeSpan.Zero), Assert.Single(item.OccurrenceOverrides!).StartsAt);
        Assert.Equal([Start, new DateTimeOffset(2026, 7, 2, 14, 0, 0, TimeSpan.Zero), Start.AddDays(3)],
            new RecurrenceExpander().Expand(item, Start, Start.AddDays(4)));
        Assert.Equal("Renamed", (await query.LoadAsync<CalendarItem>(plain))!.Title);
    }

    private async Task<int> CountRowsAsync(string where)
    {
        await using var conn = new NpgsqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"select count(*) from cal.mt_events where type in ('item_scheduled', 'item_imported', 'item_revised') and ({where})", conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
