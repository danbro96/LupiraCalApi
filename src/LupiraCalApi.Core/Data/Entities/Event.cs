using NpgsqlTypes;

namespace LupiraCalApi.Data.Entities;

public class Event
{
    public Guid Id { get; set; }
    public Guid CalendarId { get; set; }
    public string IcalUid { get; set; } = null!;        // stable per-event id; also the DAV resource name
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? Status { get; set; }                 // 'confirmed' | 'tentative' | 'cancelled'
    public string? Organizer { get; set; }
    public string? Attendees { get; set; }              // jsonb
    public bool IsAllDay { get; set; }

    // timed events (null for all-day)
    public DateTimeOffset? StartsAt { get; set; }       // UTC instant
    public DateTimeOffset? EndsAt { get; set; }
    public string? StartTimezone { get; set; }          // original IANA zone (kept so 09:00 stays 09:00 across DST)
    public string? EndTimezone { get; set; }

    // all-day events (null for timed)
    public DateOnly? StartDate { get; set; }            // date only, tz-independent
    public DateOnly? EndDate { get; set; }
    public TimeSpan? Duration { get; set; }

    // recurrence (all null for a one-off)
    public string? RecurrenceRule { get; set; }
    public DateTimeOffset[]? RecurrenceExtraDates { get; set; }
    public DateTimeOffset[]? RecurrenceExcludedDates { get; set; }
    public string? RecurrenceOverrides { get; set; }    // jsonb, keyed by original start
    public DateTimeOffset? RecurrenceEndsAt { get; set; } // watermark; null = forever

    // storage + change tracking
    public string SourceIcalendar { get; set; } = null!; // exact client blob; GET returns verbatim
    public string ContentHash { get; set; } = null!;     // hash of SourceIcalendar; emitted as the HTTP ETag
    public string[]? Tags { get; set; }
    public string Metadata { get; set; } = "{}";         // jsonb; free-form agent annotations
    public NpgsqlTsVector SearchVector { get; set; } = null!; // generated (title+description+location); GIN-indexed

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
