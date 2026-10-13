namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Change one occurrence of a recurring item and every one after it. The series ends before that occurrence and,
/// unless <see cref="Excluded"/>, a new series continues from it with <see cref="Changes"/> applied; addressed at the first
/// occurrence it changes or deletes the whole series instead.</summary>
public sealed class ChangeFollowingOccurrencesRequest
{
    /// <summary>Delete the occurrence and every one after it.</summary>
    public bool Excluded { get; set; }

    /// <summary>The update for the continuing series, as for the whole item (omitted fields keep the series' values).</summary>
    public UpdateCalendarItemRequest? Changes { get; set; }

    /// <summary>Client key for the continuing series: its id derives from it, so an offline client knows the id up front and
    /// a replay lands on the same series.</summary>
    public string? SourceKey { get; set; }

    /// <summary>Client wall-clock of the edit; an edit older than the series' last schedule change is rejected (409).
    /// Omitted ⇒ server receive time.</summary>
    public DateTimeOffset? OccurredAt { get; set; }
}
