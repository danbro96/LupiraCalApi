namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Change one occurrence of a recurring item, identified by its unmodified start. <see cref="Excluded"/> removes it
/// from the series; otherwise the given members override the series for that occurrence (omitted = inherit). Each change
/// replaces the occurrence's previous one.</summary>
public sealed class ChangeOccurrenceRequest
{
    public bool Excluded { get; set; }

    public DateTimeOffset? StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    /// <summary>Tentative|Confirmed|Cancelled. Cancelled drops the occurrence while keeping it on record as cancelled.</summary>
    public string? Status { get; set; }

    /// <summary>Client wall-clock of the edit (last-writer-wins with other schedule edits). Omitted ⇒ server receive time.</summary>
    public DateTimeOffset? OccurredAt { get; set; }
}
