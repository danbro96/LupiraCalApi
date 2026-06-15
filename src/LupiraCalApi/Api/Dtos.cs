using System.Text.Json.Nodes;

namespace LupiraCalApi.Api;

// Request/response shapes for the /api surface. Records keep them immutable and OpenAPI-described.

public record CreateEventRequest(
    Guid CalendarId, string? Title, string? Description, string? Location, string? Status,
    bool IsAllDay, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? StartTimezone,
    DateOnly? StartDate, DateOnly? EndDate, string? RecurrenceRule, string[]? Tags);

public record UpdateEventRequest(
    string? Title, string? Description, string? Location, string? Status,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? RecurrenceRule, string[]? Tags);

public record EventDto(
    Guid Id, Guid CalendarId, string IcalUid, string? Title, string? Description, string? Location,
    string? Status, bool IsAllDay, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    DateOnly? StartDate, DateOnly? EndDate, string? RecurrenceRule, string[]? Tags, JsonNode? Metadata, string Etag);

/// <summary>A single concrete occurrence of an event within a search window (recurrences expanded).</summary>
public record EventOccurrenceDto(
    Guid Id, Guid CalendarId, string? Title, string? Location, bool IsAllDay,
    DateTimeOffset Start, DateTimeOffset? End, string Etag);

public record CreateCalendarRequest(string Slug, string? DisplayName, string Kind, string? Color, string? DefaultTimezone);

public record ContainerDto(Guid Id, string Kind, Guid OwnerId, string Slug, string? DisplayName, string Access);

public record CreateContactRequest(
    Guid AddressBookId, string FullName, string? GivenName, string? FamilyName, string? Organization,
    string[]? Emails, string[]? Phones, DateOnly? Birthday, string[]? Tags);

public record ContactDto(
    Guid Id, Guid AddressBookId, string VcardUid, string? FullName, string? Organization,
    DateOnly? Birthday, string[]? Tags, JsonNode? Metadata, string Etag);
