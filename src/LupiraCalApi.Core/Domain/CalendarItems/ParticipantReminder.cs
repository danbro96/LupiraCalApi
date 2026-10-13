using System.Diagnostics.CodeAnalysis;
using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>One participant's reminder for an item. <see cref="MinutesBefore"/> is set only for
/// <see cref="ReminderMode.Before"/>; an attendee without one follows the default.</summary>
public sealed record ParticipantReminder(ReminderMode Mode, int? MinutesBefore)
{
    /// <summary>Four weeks.</summary>
    public const int MaxMinutesBefore = 40320;

    private static readonly string ModeTokens = string.Join(", ", Enum.GetNames<ReminderMode>());

    public static bool TryParse(string? mode, int? minutesBefore, [NotNullWhen(true)] out ParticipantReminder? reminder, [NotNullWhen(false)] out string? error)
    {
        reminder = null;
        if (string.IsNullOrWhiteSpace(mode)
            || char.IsDigit(mode.Trim()[0])
            || !Enum.TryParse<ReminderMode>(mode.Trim(), ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            error = $"Unknown mode '{mode}'. Valid values: {ModeTokens}.";
            return false;
        }

        if (parsed == ReminderMode.Before && minutesBefore is not (>= 0 and <= MaxMinutesBefore))
        {
            error = $"minutesBefore must be 0..{MaxMinutesBefore} when mode is Before.";
            return false;
        }

        if (parsed != ReminderMode.Before && minutesBefore is not null)
        {
            error = "minutesBefore applies only when mode is Before.";
            return false;
        }

        reminder = new ParticipantReminder(parsed, minutesBefore);
        error = null;
        return true;
    }
}
