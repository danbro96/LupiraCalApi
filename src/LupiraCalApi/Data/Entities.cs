namespace LupiraCalApi.Data;

// Domain-readable entities. C# is PascalCase; EFCore.NamingConventions maps to snake_case columns/tables
// in the `cal` schema (see CalDbContext). CalDAV/CardDAV vocabulary stays out of here — it lives in the
// DAV serialization layer. Calendars and address books are split into concrete tables so every FK is real.

/// <summary>A family member (or the shared `family` principal). Stable id is the ownership key; email is a mutable lookup attribute.</summary>
public class User
{
    public Guid Id { get; set; }
    public string AuthentikSub { get; set; } = null!;   // durable external anchor (OIDC sub)
    public string Email { get; set; } = null!;          // lowercased; DAV login + display only
    public bool IsShared { get; set; }                  // true for the shared `family` principal
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Calendar
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Slug { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Color { get; set; }
    public string? DefaultTimezone { get; set; }        // IANA zone, e.g. 'Europe/Stockholm'
    public long Revision { get; set; }                  // bumped on any child change; DAV derives ctag + sync-token from this
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class AddressBook
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Slug { get; set; } = null!;
    public string? DisplayName { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class CalendarShare
{
    public Guid CalendarId { get; set; }
    public Guid UserId { get; set; }
    public string Access { get; set; } = "read";        // 'read' | 'read-write'
}

public class AddressBookShare
{
    public Guid AddressBookId { get; set; }
    public Guid UserId { get; set; }
    public string Access { get; set; } = "read";
}

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

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public class Contact
{
    public Guid Id { get; set; }
    public Guid AddressBookId { get; set; }
    public string VcardUid { get; set; } = null!;       // stable per-contact id; also the DAV resource name
    public string? FullName { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? Organization { get; set; }
    public string? Emails { get; set; }                 // jsonb
    public string? Phones { get; set; }                 // jsonb
    public string? Addresses { get; set; }              // jsonb
    public DateOnly? Birthday { get; set; }
    public string? PhotoUrl { get; set; }               // presigned MinIO link, not bytes
    public string SourceVcard { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public string[]? Tags { get; set; }
    public string Metadata { get; set; } = "{}";        // jsonb
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

// Append-only change logs + tombstones backing the CalDAV/CardDAV sync-collection REPORT.
public class CalendarChange
{
    public Guid CalendarId { get; set; }
    public long Revision { get; set; }
    public string ItemIcalUid { get; set; } = null!;
    public string ChangeType { get; set; } = null!;     // 'saved' | 'deleted'
    public string? ContentHash { get; set; }            // null for deletes
}

public class ContactChange
{
    public Guid AddressBookId { get; set; }
    public long Revision { get; set; }
    public string ItemVcardUid { get; set; } = null!;
    public string ChangeType { get; set; } = null!;
    public string? ContentHash { get; set; }
}

/// <summary>Cross-domain edges, e.g. event → LupiraTasks item (by reference string, not FK — separate DBs).</summary>
public class Relation
{
    public Guid Id { get; set; }
    public string FromKind { get; set; } = null!;       // 'event' | 'contact'
    public Guid FromId { get; set; }
    public string ToKind { get; set; } = null!;         // 'task' | 'contact' | 'url'
    public string ToRef { get; set; } = null!;
    public string RelationType { get; set; } = null!;   // 'attendee' | 'related-to' | 'derived-from'
    public string? Metadata { get; set; }               // jsonb
}
