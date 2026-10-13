using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.CalendarItems.Events;
using LupiraCalApi.Core.Domain.Shared;
using Xunit;
using static LupiraCalApi.UnitTests.TestEvents;

namespace LupiraCalApi.UnitTests;

public class ParticipantReminderTests
{
    private static readonly Guid ItemId = Guid.NewGuid();

    private static (CalendarItem Item, Guid ParticipationId) Invited()
    {
        var pid = Guid.NewGuid();
        var i = new CalendarItem();
        i.Apply(Ev(new ItemScheduled(ItemId, "u@x", new CalendarItemFields(
            "Mtg", null, null, false, T0, T0.AddHours(1), "UTC", null, null, null, null, null, null, null, null, null, null, null, null), null)));
        i.Apply(Ev(new AttendeeInvited(ItemId, pid, Guid.NewGuid(), ParticipationRole.RequiredParticipant, T0)));
        return (i, pid);
    }

    private static ParticipantReminderSet Set(Guid pid, ReminderMode mode, int? minutes, DateTimeOffset? occurredAt = null, Guid? commandId = null) =>
        new(ItemId, pid, new ParticipantReminder(mode, minutes), occurredAt, commandId);

    [Fact]
    public void Setting_a_lead_time_lands_on_the_participation()
    {
        var (i, pid) = Invited();
        i.Apply(Ev(Set(pid, ReminderMode.Before, 15)));

        Assert.Equal(new ParticipantReminder(ReminderMode.Before, 15), Assert.Single(i.Attendees).Reminder);
    }

    [Fact]
    public void Default_is_stored_as_no_reminder()
    {
        var (i, pid) = Invited();
        i.Apply(Ev(Set(pid, ReminderMode.Off, null)));
        i.Apply(Ev(Set(pid, ReminderMode.Default, null)));

        Assert.Null(Assert.Single(i.Attendees).Reminder);
    }

    [Fact]
    public void An_older_occurredAt_loses_even_when_applied_later()
    {
        var (i, pid) = Invited();
        i.Apply(Ev(Set(pid, ReminderMode.Before, 30, T0.AddMinutes(10), Guid.NewGuid())));
        i.Apply(Ev(Set(pid, ReminderMode.Off, null, T0.AddMinutes(5), Guid.NewGuid())));

        var a = Assert.Single(i.Attendees);
        Assert.Equal(new ParticipantReminder(ReminderMode.Before, 30), a.Reminder);
        Assert.Equal(T0.AddMinutes(10), a.ReminderTs);
    }

    [Fact]
    public void A_newer_occurredAt_wins()
    {
        var (i, pid) = Invited();
        i.Apply(Ev(Set(pid, ReminderMode.Before, 30, T0.AddMinutes(5), Guid.NewGuid())));
        i.Apply(Ev(Set(pid, ReminderMode.Off, null, T0.AddMinutes(10), Guid.NewGuid())));

        Assert.Equal(ReminderMode.Off, Assert.Single(i.Attendees).Reminder!.Mode);
    }

    [Fact]
    public void A_replay_of_the_winning_stamp_changes_nothing()
    {
        var (i, pid) = Invited();
        var cmd = Guid.NewGuid();
        i.Apply(Ev(Set(pid, ReminderMode.Before, 30, T0.AddMinutes(5), cmd)));
        i.Apply(Ev(Set(pid, ReminderMode.Off, null, T0.AddMinutes(5), cmd)));

        Assert.Equal(ReminderMode.Before, Assert.Single(i.Attendees).Reminder!.Mode);
    }

    [Fact]
    public void Guards_are_per_participation()
    {
        var (i, pid) = Invited();
        var other = Guid.NewGuid();
        i.Apply(Ev(new AttendeeInvited(ItemId, other, Guid.NewGuid(), ParticipationRole.RequiredParticipant, T0)));
        i.Apply(Ev(Set(pid, ReminderMode.Before, 30, T0.AddMinutes(10), Guid.NewGuid())));
        i.Apply(Ev(Set(other, ReminderMode.Off, null, T0.AddMinutes(5), Guid.NewGuid())));

        Assert.Equal(ReminderMode.Off, i.Attendees.Single(a => a.ParticipationId == other).Reminder!.Mode);
    }

    [Fact]
    public void An_unknown_participation_is_ignored()
    {
        var (i, _) = Invited();
        i.Apply(Ev(Set(Guid.NewGuid(), ReminderMode.Off, null)));

        Assert.Null(Assert.Single(i.Attendees).Reminder);
    }

    [Theory]
    [InlineData("Before", 0, ReminderMode.Before)]
    [InlineData("before", 40320, ReminderMode.Before)]
    [InlineData("Off", null, ReminderMode.Off)]
    [InlineData("default", null, ReminderMode.Default)]
    public void Valid_inputs_parse(string mode, int? minutes, ReminderMode expected)
    {
        Assert.True(ParticipantReminder.TryParse(mode, minutes, out var reminder, out _));
        Assert.Equal(new ParticipantReminder(expected, minutes), reminder);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("Later", null)]
    [InlineData("2", null)]
    [InlineData("Before", null)]
    [InlineData("Before", -1)]
    [InlineData("Before", 40321)]
    [InlineData("Off", 10)]
    [InlineData("Default", 0)]
    public void Invalid_inputs_are_rejected(string? mode, int? minutes)
    {
        Assert.False(ParticipantReminder.TryParse(mode, minutes, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
