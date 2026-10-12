using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.CalendarItems.Events;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>A participant joining an item that has already ended counts as attended at its end; an explicit choice wins,
/// and an attendee already attended is not confirmed again.</summary>
public class AttendanceOnJoinTests
{
    private static readonly RecurrenceExpander Expander = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 15, 0, 0, TimeSpan.Zero);

    private static CalendarItem Timed(DateTimeOffset start, TimeSpan length, string? rule = null) => new()
    {
        Id = Guid.NewGuid(), StartsAt = start, EndsAt = start + length, StartTimezone = "UTC", RecurrenceRule = rule,
    };

    private static CalendarItem AllDay(DateOnly start, DateOnly end) => new() { Id = Guid.NewGuid(), IsAllDay = true, StartDate = start, EndDate = end };

    private static DateTimeOffset? Stamp(CalendarItem item, bool? attended = null) =>
        ParticipationService.AttendanceStamp(item, attended, Now, Expander);

    [Fact]
    public void An_ended_item_is_attended_at_its_end()
    {
        var item = Timed(Now.AddDays(-2), TimeSpan.FromHours(2));
        Assert.Equal(item.EndsAt, Stamp(item));
    }

    [Fact]
    public void A_future_item_is_not_attended() =>
        Assert.Null(Stamp(Timed(Now.AddDays(1), TimeSpan.FromHours(1))));

    [Fact]
    public void An_item_still_running_is_not_attended() =>
        Assert.Null(Stamp(Timed(Now.AddHours(-1), TimeSpan.FromHours(2))));

    [Fact]
    public void An_all_day_item_ending_today_is_not_attended() =>
        Assert.Null(Stamp(AllDay(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12))));

    [Fact]
    public void An_all_day_item_ending_yesterday_is_attended_at_the_start_of_today() =>
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero), Stamp(AllDay(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 11))));

    [Fact]
    public void An_item_without_a_start_is_not_attended() =>
        Assert.Null(Stamp(new CalendarItem { Id = Guid.NewGuid() }));

    [Fact]
    public void A_series_without_an_end_is_not_attended() =>
        Assert.Null(Stamp(Timed(Now.AddYears(-1), TimeSpan.FromHours(1), "FREQ=WEEKLY")));

    [Fact]
    public void A_finished_series_is_attended_at_its_last_occurrence_end()
    {
        var item = Timed(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(1), "FREQ=WEEKLY;COUNT=3");
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero), Stamp(item));
    }

    [Fact]
    public void A_series_with_occurrences_ahead_is_not_attended() =>
        Assert.Null(Stamp(Timed(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(1), "FREQ=WEEKLY;COUNT=5")));

    [Fact]
    public void Explicit_false_wins_over_an_ended_item() =>
        Assert.Null(Stamp(Timed(Now.AddDays(-2), TimeSpan.FromHours(1)), attended: false));

    [Fact]
    public void Explicit_true_on_a_future_item_is_attended_now() =>
        Assert.Equal(Now, Stamp(Timed(Now.AddDays(1), TimeSpan.FromHours(1)), attended: true));

    [Fact]
    public void Joining_an_ended_item_appends_the_invite_and_the_attendance()
    {
        var item = Timed(Now.AddDays(-2), TimeSpan.FromHours(1));
        var contact = Guid.NewGuid();

        var (events, added, _) = ParticipationService.PlanAdditions(item, [contact], Now, Stamp(item));

        var pid = Assert.Single(added).ParticipationId;
        Assert.Collection(
            events,
            e => Assert.Equal(Now, Assert.IsType<AttendeeInvited>(e).At),
            e => Assert.Equal(new AttendanceConfirmed(item.Id, pid, item.EndsAt!.Value), e));
    }

    [Fact]
    public void Joining_a_future_item_appends_only_the_invite()
    {
        var item = Timed(Now.AddDays(1), TimeSpan.FromHours(1));

        var (events, _, _) = ParticipationService.PlanAdditions(item, [Guid.NewGuid()], Now, Stamp(item));

        Assert.IsType<AttendeeInvited>(Assert.Single(events));
    }

    [Fact]
    public void An_attendee_already_attended_is_not_confirmed_again()
    {
        var item = Timed(Now.AddDays(-2), TimeSpan.FromHours(1));
        var (attended, pending) = (Guid.NewGuid(), Guid.NewGuid());
        item.Attendees.Add(new ItemAttendee { ParticipationId = Guid.NewGuid(), ContactId = attended, AttendedAt = Now.AddDays(-2) });
        var pendingPid = Guid.NewGuid();
        item.Attendees.Add(new ItemAttendee { ParticipationId = pendingPid, ContactId = pending });

        var (events, added, alreadyPresent) = ParticipationService.PlanAdditions(item, [attended, pending], Now, Stamp(item));

        Assert.Empty(added);
        Assert.Equal(2, alreadyPresent);
        Assert.Equal(pendingPid, Assert.IsType<AttendanceConfirmed>(Assert.Single(events)).ParticipationId);
    }
}
