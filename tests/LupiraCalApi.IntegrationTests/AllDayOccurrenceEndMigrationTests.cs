using LupiraCalApi.Core.Data.Migrations;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.CalendarItems.Events;
using Marten;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>The one-shot conversion against Postgres: all-day occurrence changes stored with the day-after end move back
/// one day, a dry run writes nothing, timed series and rows written since the fix are left alone, and a re-run touches nothing.</summary>
public sealed class AllDayOccurrenceEndMigrationTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private static DateTimeOffset Day(int month, int day) => new(2026, month, day, 0, 0, 0, TimeSpan.Zero);

    private static CalendarItemFields Series(bool allDay, OccurrenceOverride[]? overrides) =>
        new("Camp", null, null, allDay, allDay ? null : Day(7, 27).AddHours(9), allDay ? null : Day(7, 27).AddHours(10), null, null,
            allDay ? new DateOnly(2026, 7, 27) : null, allDay ? new DateOnly(2026, 7, 28) : null, "FREQ=WEEKLY;COUNT=3",
            null, null, overrides, null, null, null, null, null);

    [Fact]
    public async Task Old_all_day_change_ends_move_back_one_day_once_and_rebuild_inclusive()
    {
        var imported = Guid.NewGuid();
        var changed = Guid.NewGuid();
        var timed = Guid.NewGuid();
        var recent = Guid.NewGuid();
        var dayAfter = new OccurrenceOverride(Day(8, 3), null, Day(8, 6), null, null, null, null);

        await using (var old = Store.LightweightSession())
        {
            old.Events.StartStream<CalendarItem>(imported, new ItemImported(imported, "imported@x", Series(true, [dayAfter])));
            old.Events.StartStream<CalendarItem>(changed, new ItemScheduled(changed, "changed@x", Series(true, null), null));
            old.Events.StartStream<CalendarItem>(timed, new ItemImported(timed, "timed@x",
                Series(false, [new OccurrenceOverride(Day(8, 3).AddHours(9), null, Day(8, 3).AddHours(12), null, null, null, null)])));
            await old.SaveChangesAsync();
        }

        await using (var old = Store.LightweightSession())
        {
            old.Events.Append(changed, new OccurrenceOverridden(changed, dayAfter));
            await old.SaveChangesAsync();
        }

        await using (var fixedSession = Store.LightweightSession())
        {
            fixedSession.SetHeader(AllDayOccurrenceEndMigration.InclusiveEndHeader, true);
            fixedSession.Events.StartStream<CalendarItem>(recent, new ItemImported(recent, "recent@x", Series(true, [dayAfter])));
            await fixedSession.SaveChangesAsync();
        }

        var (dryCount, samples) = await AllDayOccurrenceEndMigration.RunAsync(Store, Factory.ConnectionString, dryRun: true, CancellationToken.None);
        Assert.Equal(2, dryCount);
        Assert.All(samples, s => Assert.Contains("2026-08-03 ends 2026-08-06 -> 2026-08-05", s));
        Assert.Equal(Day(8, 6), await EndAsync(imported));

        Assert.Equal(2, (await AllDayOccurrenceEndMigration.RunAsync(Store, Factory.ConnectionString, dryRun: false, CancellationToken.None)).Count);
        Assert.Equal(0, (await AllDayOccurrenceEndMigration.RunAsync(Store, Factory.ConnectionString, dryRun: false, CancellationToken.None)).Count);

        using var daemon = await Store.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<CalendarItem>(CancellationToken.None);

        Assert.Equal(Day(8, 5), await EndAsync(imported));
        Assert.Equal(Day(8, 5), await EndAsync(changed));
        Assert.Equal(Day(8, 3).AddHours(12), await EndAsync(timed));
        Assert.Equal(Day(8, 6), await EndAsync(recent));
    }

    private async Task<DateTimeOffset?> EndAsync(Guid itemId)
    {
        await using var query = Store.QuerySession();
        return Assert.Single((await query.LoadAsync<CalendarItem>(itemId))!.OccurrenceOverrides!).EndsAt;
    }
}
