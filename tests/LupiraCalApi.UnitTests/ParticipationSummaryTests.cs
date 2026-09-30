using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.CalendarItems.Events;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using Xunit;
using static LupiraCalApi.UnitTests.TestEvents;

namespace LupiraCalApi.UnitTests;

/// <summary>Aggregation rules of <see cref="ParticipationService.Summarize"/>: readable-calendar scoping,
/// withdrawn-attendee exclusion, per-contact counting, recency weighting, window filtering, and ordering.</summary>
public class ParticipationSummaryTests
{
    private static readonly Guid ReadableCal = Guid.NewGuid();
    private static readonly Guid ForeignCal = Guid.NewGuid();

    private static readonly RecurrenceExpander Expander = new();

    private static CalendarItemFields Fields(DateTimeOffset start, string? rule = null) => new(
        "Lunch", null, ItemStatus.Confirmed, false, start, start.AddHours(1),
        "UTC", null, null, null, rule, null, null, null, ItemCategory.General, null, null, null, null);

    private static CalendarItem Item(Guid calendarId, DateTimeOffset start, params Guid[] contactIds) =>
        Series(calendarId, start, null, contactIds);

    private static CalendarItem Series(Guid calendarId, DateTimeOffset start, string? rule, params Guid[] contactIds)
    {
        var id = Guid.NewGuid();
        var i = new CalendarItem();
        i.Apply(Ev(new ItemScheduled(id, $"{id:N}@x", Fields(start, rule), null)));
        i.Apply(Ev(new AddedToCalendar(id, calendarId, CalendarEntryStatus.Accepted, DateTimeOffset.UtcNow)));
        foreach (var c in contactIds)
            i.Apply(Ev(new AttendeeInvited(id, Guid.NewGuid(), c, ParticipationRole.RequiredParticipant, start)));
        return i;
    }

    private static readonly DateTimeOffset T1 = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T3 = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static List<ParticipationSummaryEntry> Summarize(IEnumerable<CalendarItem> items, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        ParticipationService.Summarize(items, [ReadableCal], from, to, Now, Expander);

    [Fact]
    public void Counts_per_contact_and_tracks_the_latest_start()
    {
        var (anna, johan) = (Guid.NewGuid(), Guid.NewGuid());
        var items = new[]
        {
            Item(ReadableCal, T1, anna, johan),
            Item(ReadableCal, T2, johan),
            Item(ReadableCal, T3, anna, johan),
        };

        var s = Summarize(items);

        Assert.Equal([johan, anna], s.Select(e => e.ContactId));
        Assert.Equal(3, s[0].Count);
        Assert.Equal(2, s[1].Count);
        Assert.Equal(T3, s[0].LastAt);
        Assert.Equal(T3, s[1].LastAt);
    }

    [Fact]
    public void Unreadable_calendars_do_not_contribute()
    {
        var anna = Guid.NewGuid();
        var items = new[] { Item(ReadableCal, T1, anna), Item(ForeignCal, T2, anna) };

        var s = Summarize(items);

        Assert.Equal(1, Assert.Single(s).Count);
        Assert.Equal(T1, s[0].LastAt);
    }

    [Fact]
    public void Withdrawn_attendee_does_not_count()
    {
        var anna = Guid.NewGuid();
        var item = Item(ReadableCal, T1, anna);
        item.Apply(Ev(new ParticipantLeft(item.Id, item.Attendees[0].ParticipationId, T1)));

        Assert.Empty(Summarize([item]));
    }

    [Fact]
    public void Window_filters_on_occurrence_start()
    {
        var anna = Guid.NewGuid();
        var items = new[] { Item(ReadableCal, T1, anna), Item(ReadableCal, T3, anna) };

        var s = Summarize(items, from: T2);

        Assert.Equal(1, Assert.Single(s).Count);
        Assert.Equal(T3, s[0].LastAt);
    }

    [Fact]
    public void Startless_items_count_only_without_a_window()
    {
        var anna = Guid.NewGuid();
        var i = new CalendarItem();
        var id = Guid.NewGuid();
        i.Apply(Ev(new ItemScheduled(id, $"{id:N}@x", new CalendarItemFields(
            "Sometime", null, null, false, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null), null)));
        i.Apply(Ev(new AddedToCalendar(id, ReadableCal, CalendarEntryStatus.Accepted, DateTimeOffset.UtcNow)));
        i.Apply(Ev(new AttendeeInvited(id, Guid.NewGuid(), anna, ParticipationRole.RequiredParticipant, T1)));

        Assert.Single(Summarize([i]));
        Assert.Empty(Summarize([i], from: T1));
    }

    [Fact]
    public void Recent_meetings_outrank_many_old_ones()
    {
        var (anna, erik) = (Guid.NewGuid(), Guid.NewGuid());
        var twoYearsAgo = Now.AddYears(-2);
        var items = Enumerable.Range(0, 5).Select(d => Item(ReadableCal, twoYearsAgo.AddDays(d), anna))
            .Append(Item(ReadableCal, Now.AddDays(-10), erik))
            .Append(Item(ReadableCal, Now.AddDays(-20), erik));

        var s = Summarize(items);

        Assert.Equal([erik, anna], s.Select(e => e.ContactId));
        Assert.Equal(5, s[1].Count);
        Assert.True(s[0].Score > s[1].Score * 10);
    }

    [Fact]
    public void Past_occurrence_decays_with_a_90_day_half_life()
    {
        var anna = Guid.NewGuid();

        var s = Summarize([Item(ReadableCal, Now.AddDays(-90), anna)]);

        Assert.Equal(0.5, Assert.Single(s).Score, 3);
    }

    [Fact]
    public void Planned_item_weighs_one()
    {
        var anna = Guid.NewGuid();

        var s = Summarize([Item(ReadableCal, Now.AddDays(200), anna)]);

        Assert.Equal(1, Assert.Single(s).Score, 3);
    }

    [Fact]
    public void Series_scores_each_past_occurrence_and_only_its_next_planned_one()
    {
        var (anna, erik) = (Guid.NewGuid(), Guid.NewGuid());
        var weekly = Series(ReadableCal, Now.AddDays(-7 * 8).AddHours(1), "FREQ=WEEKLY", anna);
        var oneOff = Item(ReadableCal, Now.AddDays(-1), erik);

        var s = Summarize([weekly, oneOff]);

        Assert.Equal([anna, erik], s.Select(e => e.ContactId));
        var pastWeeks = Enumerable.Range(1, 8).Sum(w => Math.Pow(0.5, ((w * 7) - (1 / 24.0)) / 90));
        Assert.Equal(pastWeeks + 1, s[0].Score, 3);
        Assert.Equal(1, s[0].Count);
    }

    [Fact]
    public void Window_bounds_the_score_too()
    {
        var anna = Guid.NewGuid();
        var weekly = Series(ReadableCal, Now.AddDays(-7 * 8).AddHours(1), "FREQ=WEEKLY", anna);

        var s = Summarize([weekly], to: Now.AddDays(-7 * 8).AddHours(2));

        Assert.Equal(Math.Pow(0.5, ((7 * 8) - (1 / 24.0)) / 90), Assert.Single(s).Score, 3);
    }
}
