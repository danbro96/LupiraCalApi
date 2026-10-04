using LupiraCalApi.Core.Data.Migrations;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.CalendarItems.Events;
using Marten;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>The one-shot conversion against Postgres: imports stored with the day-after end move back one day, a dry run
/// writes nothing, imports written since the fix are left alone, and a re-run touches nothing.</summary>
public sealed class ImportedAllDayEndMigrationTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private static CalendarItemFields AllDay(string title, DateOnly start, DateOnly? end) =>
        new(title, null, null, true, null, null, null, null, start, end, null, null, null, null, null, null, null, null, null);

    [Fact]
    public async Task Old_imports_move_back_one_day_once_and_rebuild_inclusive()
    {
        var trip = Guid.NewGuid();
        var oneDay = Guid.NewGuid();
        var openEnded = Guid.NewGuid();
        var recent = Guid.NewGuid();
        var start = new DateOnly(2026, 7, 10);

        await using (var old = Store.LightweightSession())
        {
            old.Events.StartStream<CalendarItem>(trip, new ItemImported(trip, "trip@x", AllDay("Trip", start, start.AddDays(4))));
            old.Events.StartStream<CalendarItem>(oneDay, new ItemImported(oneDay, "day@x", AllDay("Day", start, start)));
            old.Events.StartStream<CalendarItem>(openEnded, new ItemImported(openEnded, "open@x", AllDay("Open", start, null)));
            await old.SaveChangesAsync();
        }

        await using (var fixedSession = Store.LightweightSession())
        {
            fixedSession.SetHeader(ImportedAllDayEndMigration.InclusiveEndHeader, true);
            fixedSession.Events.StartStream<CalendarItem>(recent, new ItemImported(recent, "recent@x", AllDay("Recent", start, start.AddDays(4))));
            await fixedSession.SaveChangesAsync();
        }

        var (dryCount, samples) = await ImportedAllDayEndMigration.RunAsync(Store, Factory.ConnectionString, dryRun: true, CancellationToken.None);
        Assert.Equal(1, dryCount);
        Assert.Contains("2026-07-10..2026-07-14 -> 2026-07-10..2026-07-13", Assert.Single(samples));
        Assert.Equal(start.AddDays(4), await StoredEndAsync(trip));

        Assert.Equal(1, (await ImportedAllDayEndMigration.RunAsync(Store, Factory.ConnectionString, dryRun: false, CancellationToken.None)).Count);
        Assert.Equal(0, (await ImportedAllDayEndMigration.RunAsync(Store, Factory.ConnectionString, dryRun: false, CancellationToken.None)).Count);

        using var daemon = await Store.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<CalendarItem>(CancellationToken.None);

        await using var query = Store.QuerySession();
        Assert.Equal(start.AddDays(3), (await query.LoadAsync<CalendarItem>(trip))!.EndDate);
        Assert.Equal(start, (await query.LoadAsync<CalendarItem>(oneDay))!.EndDate);
        Assert.Null((await query.LoadAsync<CalendarItem>(openEnded))!.EndDate);
        Assert.Equal(start.AddDays(4), (await query.LoadAsync<CalendarItem>(recent))!.EndDate);
    }

    private async Task<DateOnly?> StoredEndAsync(Guid stream)
    {
        await using var query = Store.QuerySession();
        var e = Assert.Single(await query.Events.FetchStreamAsync(stream));
        return Assert.IsType<ItemImported>(e.Data).Parsed.EndDate;
    }
}
