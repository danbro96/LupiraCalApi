using LupiraCalApi.Core.Data.Migrations;
using LupiraCalApi.Core.Domain.Shared;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>The one-shot conversion of raw deviation text into structured deviations; everything else carries over.</summary>
public class OccurrenceDeviationMigrationTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private static CalendarItemFieldsV1 Old(string? exceptions, string? overrides) => new(
        "Master", "d", ItemStatus.Confirmed, false, Start, Start.AddHours(1), "Europe/Stockholm", null, null, null, "FREQ=DAILY",
        exceptions, overrides, ItemCategory.Meal, Guid.Empty, "Home", null, ["tag"], DatePrecision.Exact, null);

    [Fact]
    public void Raw_deviation_text_becomes_structured_deviations()
    {
        var f = OccurrenceDeviationMigration.Convert(Old("EXDATE:20260703T090000Z",
            "BEGIN:VEVENT\nUID:e1@x\nRECURRENCE-ID:20260702T090000Z\nDTSTART:20260702T100000Z\nDTEND:20260702T110000Z\nSUMMARY:Master\nSTATUS:CANCELLED\nEND:VEVENT"));

        Assert.Equal([new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero)], f.ExcludedOccurrences!);
        var o = Assert.Single(f.OccurrenceOverrides!);
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero), o.OriginalStart);
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero), o.StartsAt);
        Assert.Equal(ItemStatus.Cancelled, o.Status);
        Assert.Null(o.Title);   // same as the series
    }

    [Fact]
    public void Fields_without_deviation_text_carry_over_unchanged()
    {
        var old = Old(null, null);
        var f = OccurrenceDeviationMigration.Convert(old);

        Assert.Null(f.ExcludedOccurrences);
        Assert.Null(f.ExtraOccurrences);
        Assert.Null(f.OccurrenceOverrides);
        Assert.Equal((old.Title, old.StartsAt, old.StartTimezone, old.RecurrenceRule, old.Category, old.LocationLabel, old.StartPrecision),
            (f.Title, f.StartsAt, f.StartTimezone, f.RecurrenceRule, f.Category, f.LocationLabel, f.StartPrecision));
        Assert.Equal(old.Tags, f.Tags);
    }
}
