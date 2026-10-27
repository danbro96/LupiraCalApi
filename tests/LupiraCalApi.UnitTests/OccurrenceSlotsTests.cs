using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>The occurrences search reports for a window, shared by stored items and subscribed events.</summary>
public class OccurrenceSlotsTests
{
    private static readonly RecurrenceExpander Expander = new();

    [Fact]
    public void Window_matches_occurrences_overlapping_it_not_only_starting_in_it()
    {
        var (from, to) = (Utc(2026, 5, 30, 6, 46), Utc(2026, 5, 30, 8, 46));
        var weekend = new CalendarItem { StartsAt = Utc(2026, 5, 30, 4, 45), EndsAt = Utc(2026, 5, 31, 14), StartTimezone = "UTC" };
        var breakfast = new CalendarItem { StartsAt = Utc(2026, 5, 30, 4, 45), EndsAt = Utc(2026, 5, 30, 5, 45), StartTimezone = "UTC" };
        var nightly = new CalendarItem { StartsAt = Utc(2026, 5, 27, 22), EndsAt = Utc(2026, 5, 28, 8), StartTimezone = "UTC", RecurrenceRule = "FREQ=DAILY;COUNT=5" };
        var holiday = new CalendarItem { IsAllDay = true, StartDate = new DateOnly(2026, 5, 30), EndDate = new DateOnly(2026, 5, 30) };

        Assert.Equal(Utc(2026, 5, 31, 14), Assert.Single(Slots(weekend, from, to)).End);
        Assert.Empty(Slots(breakfast, from, to));
        var night = Assert.Single(Slots(nightly, from, to));
        Assert.Equal(Utc(2026, 5, 29, 22), night.Start);
        Assert.Equal(Utc(2026, 5, 30, 8), night.End);
        Assert.Equal(Utc(2026, 5, 29, 22), night.OriginalStart);
        Assert.Equal(Utc(2026, 5, 30), Assert.Single(Slots(holiday, from, to)).Start);
    }

    [Fact]
    public void Multi_day_all_day_occurrence_ends_on_its_inclusive_last_day()
    {
        var trip = new CalendarItem { IsAllDay = true, StartDate = new DateOnly(2026, 7, 16), EndDate = new DateOnly(2026, 7, 18) };

        var slot = Assert.Single(Slots(trip, Utc(2026, 7, 1), Utc(2026, 7, 31)));

        Assert.Equal(Utc(2026, 7, 16), slot.Start);
        Assert.Equal(Utc(2026, 7, 18), slot.End);
        Assert.Null(slot.OriginalStart);
        Assert.Null(slot.Change);
    }

    [Fact]
    public void A_one_off_without_an_end_reports_none_and_matches_at_its_start()
    {
        var reminder = new CalendarItem { StartsAt = Utc(2026, 7, 1, 9) };

        var slot = Assert.Single(Slots(reminder, Utc(2026, 7, 1, 9), Utc(2026, 7, 1, 10)));

        Assert.Equal(Utc(2026, 7, 1, 9), slot.Start);
        Assert.Null(slot.End);
        Assert.Empty(Slots(reminder, Utc(2026, 7, 1, 9, 1), Utc(2026, 7, 1, 10)));
    }

    [Fact]
    public void A_changed_occurrence_takes_its_own_end()
    {
        var weekly = new CalendarItem
        {
            StartsAt = Utc(2026, 7, 6, 9),
            EndsAt = Utc(2026, 7, 6, 10),
            StartTimezone = "UTC",
            RecurrenceRule = "FREQ=WEEKLY;COUNT=3",
            OccurrenceOverrides = [new OccurrenceOverride(Utc(2026, 7, 13, 9), null, Utc(2026, 7, 13, 15), "Long", null, null, null)],
        };

        var slot = Assert.Single(Slots(weekly, Utc(2026, 7, 13, 9, 30), Utc(2026, 7, 13, 16)));

        Assert.Equal(Utc(2026, 7, 13, 9), slot.Start);
        Assert.Equal(Utc(2026, 7, 13, 15), slot.End);
        Assert.Equal("Long", slot.Change!.Title);
    }

    [Fact]
    public void A_cancelled_occurrence_is_left_out()
    {
        var weekly = new CalendarItem
        {
            StartsAt = Utc(2026, 7, 6, 9),
            EndsAt = Utc(2026, 7, 6, 10),
            StartTimezone = "UTC",
            RecurrenceRule = "FREQ=WEEKLY;COUNT=3",
            OccurrenceOverrides = [new OccurrenceOverride(Utc(2026, 7, 13, 9), null, null, null, null, ItemStatus.Cancelled, null)],
        };

        Assert.Equal([Utc(2026, 7, 6, 9), Utc(2026, 7, 20, 9)], Slots(weekly, Utc(2026, 7, 1), Utc(2026, 8, 1)).Select(s => s.Start));
    }

    private static DateTimeOffset Utc(int y, int m, int d, int h = 0, int min = 0) => new(y, m, d, h, min, 0, TimeSpan.Zero);

    private static List<OccurrenceSlot> Slots(CalendarItem item, DateTimeOffset from, DateTimeOffset to) =>
        [.. OccurrenceSlots.Of(item, Expander, from, to, to)];
}
