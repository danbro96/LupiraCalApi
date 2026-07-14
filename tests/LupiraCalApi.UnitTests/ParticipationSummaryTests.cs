using LupiraCalApi.Application;
using LupiraCalApi.Domain;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Aggregation rules of <see cref="ParticipationService.Summarize"/>: readable-calendar scoping,
/// withdrawn-attendee exclusion, per-contact counting, recency, window filtering, and ordering.</summary>
public class ParticipationSummaryTests
{
    static readonly Guid ReadableCal = Guid.NewGuid();
    static readonly Guid ForeignCal = Guid.NewGuid();

    static CalendarItemFields Fields(DateTimeOffset start) => new(
        "Lunch", null, ItemStatus.Confirmed, false, start, start.AddHours(1),
        "UTC", null, null, null, null, null, null, ItemCategory.General, null, null, null, null);

    static CalendarItem Item(Guid calendarId, DateTimeOffset start, params Guid[] contactIds)
    {
        var id = Guid.NewGuid();
        var i = new CalendarItem();
        i.Apply(new ItemScheduled(id, $"{id:N}@x", Fields(start), null));
        i.Apply(new AddedToCalendar(id, calendarId, CalendarEntryStatus.Accepted, DateTimeOffset.UtcNow));
        foreach (var c in contactIds)
            i.Apply(new AttendeeInvited(id, Guid.NewGuid(), c, ParticipationRole.RequiredParticipant, start));
        return i;
    }

    static readonly DateTimeOffset T1 = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset T2 = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset T3 = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Counts_per_contact_ordered_by_count_then_recency()
    {
        var (anna, johan) = (Guid.NewGuid(), Guid.NewGuid());
        var items = new[]
        {
            Item(ReadableCal, T1, anna, johan),
            Item(ReadableCal, T2, johan),
            Item(ReadableCal, T3, anna, johan),
        };

        var s = ParticipationService.Summarize(items, [ReadableCal], null, null);

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

        var s = ParticipationService.Summarize(items, [ReadableCal], null, null);

        Assert.Equal(1, Assert.Single(s).Count);
        Assert.Equal(T1, s[0].LastAt);
    }

    [Fact]
    public void Withdrawn_attendee_does_not_count()
    {
        var anna = Guid.NewGuid();
        var item = Item(ReadableCal, T1, anna);
        item.Apply(new ParticipantLeft(item.Id, item.Attendees[0].ParticipationId, T1));

        Assert.Empty(ParticipationService.Summarize([item], [ReadableCal], null, null));
    }

    [Fact]
    public void Window_filters_on_occurrence_start()
    {
        var anna = Guid.NewGuid();
        var items = new[] { Item(ReadableCal, T1, anna), Item(ReadableCal, T3, anna) };

        var s = ParticipationService.Summarize(items, [ReadableCal], T2, null);

        Assert.Equal(1, Assert.Single(s).Count);
        Assert.Equal(T3, s[0].LastAt);
    }

    [Fact]
    public void Startless_items_count_only_without_a_window()
    {
        var anna = Guid.NewGuid();
        var i = new CalendarItem();
        var id = Guid.NewGuid();
        i.Apply(new ItemScheduled(id, $"{id:N}@x", new CalendarItemFields(
            "Sometime", null, null, false, null, null, null, null, null, null, null, null, null, null, null, null, null, null), null));
        i.Apply(new AddedToCalendar(id, ReadableCal, CalendarEntryStatus.Accepted, DateTimeOffset.UtcNow));
        i.Apply(new AttendeeInvited(id, Guid.NewGuid(), anna, ParticipationRole.RequiredParticipant, T1));

        Assert.Single(ParticipationService.Summarize([i], [ReadableCal], null, null));
        Assert.Empty(ParticipationService.Summarize([i], [ReadableCal], T1, null));
    }
}
