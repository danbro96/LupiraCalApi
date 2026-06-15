using System.Text.Json.Nodes;
using LupiraCalApi.Api;
using LupiraCalApi.Data;
using LupiraCalApi.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LupiraCalApi.Domain;

/// <summary>
/// The calendar core shared by REST and MCP. Every mutation authors the canonical <c>source_icalendar</c>,
/// recomputes <c>content_hash</c>, bumps the owning calendar's revision, and appends a change-log row
/// (tombstone for deletes) so the future CalDAV sync surface has everything it needs.
/// </summary>
public sealed class EventService(CalDbContext db, AccessService access, RecurrenceExpander expander)
{
    public async Task<EventDto> CreateAsync(Guid userId, CreateEventRequest r, CancellationToken ct = default)
    {
        if (!await access.CanWriteCalendarAsync(userId, r.CalendarId, ct)) throw new AccessDeniedException();

        var e = new Event
        {
            Id = Guid.NewGuid(),
            CalendarId = r.CalendarId,
            IcalUid = $"{Guid.NewGuid():N}@cal.lupira.com",
            Title = r.Title, Description = r.Description, Location = r.Location, Status = r.Status,
            IsAllDay = r.IsAllDay, StartsAt = r.StartsAt, EndsAt = r.EndsAt, StartTimezone = r.StartTimezone,
            StartDate = r.StartDate, EndDate = r.EndDate, RecurrenceRule = r.RecurrenceRule, Tags = r.Tags,
            Metadata = "{}",
        };
        e.SourceIcalendar = ICalSerializer.ToICalendar(e);
        e.ContentHash = ContentHash.Of(e.SourceIcalendar);

        db.Events.Add(e);
        await BumpAndLogAsync(e.CalendarId, e.IcalUid, "saved", e.ContentHash, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    public async Task<List<EventOccurrenceDto>> SearchAsync(
        Guid userId, string? query, DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId,
        string? tag = null, string? metadataContains = null, CancellationToken ct = default)
    {
        var calIds = await access.AccessibleCalendars(userId).Select(c => c.Id).ToListAsync(ct);
        if (calendarId is { } cid)
        {
            if (!calIds.Contains(cid)) throw new AccessDeniedException();
            calIds = [cid];
        }

        var q = db.Events.Where(e => calIds.Contains(e.CalendarId) && e.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            // Ranked full-text (generated tsvector) OR typo-tolerant trigram word-similarity on the title
            // (word_similarity matches a query word inside the longer title; 0.3 catches single-letter typos).
            q = q.Where(e => e.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("english", term))
                || EF.Functions.TrigramsWordSimilarity(term, e.Title ?? "") >= 0.3);
        }
        if (!string.IsNullOrWhiteSpace(tag))
            q = q.Where(e => e.Tags != null && e.Tags.Contains(tag));
        if (!string.IsNullOrWhiteSpace(metadataContains))
            q = q.Where(e => EF.Functions.JsonContains(e.Metadata, metadataContains));
        var candidates = await q.ToListAsync(ct);

        var windowStart = from ?? DateTimeOffset.UtcNow.AddYears(-1);
        var windowEnd = to ?? DateTimeOffset.UtcNow.AddYears(1);
        var results = new List<EventOccurrenceDto>();

        foreach (var e in candidates)
        {
            TimeSpan? duration = (e.StartsAt is { } s && e.EndsAt is { } en) ? en - s : null;

            if (!string.IsNullOrWhiteSpace(e.RecurrenceRule))
            {
                foreach (var occ in expander.Expand(e.SourceIcalendar, windowStart, windowEnd))
                    results.Add(new EventOccurrenceDto(e.Id, e.CalendarId, e.Title, e.Location, e.IsAllDay,
                        occ, duration is { } d ? occ + d : null, e.ContentHash));
            }
            else if (OccurrenceStart(e) is { } start && start >= windowStart && start < windowEnd)
            {
                results.Add(new EventOccurrenceDto(e.Id, e.CalendarId, e.Title, e.Location, e.IsAllDay,
                    start, duration is { } d ? start + d : e.EndsAt, e.ContentHash));
            }
        }
        return [.. results.OrderBy(r => r.Start)];
    }

    public async Task<EventDto?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var e = await db.Events.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
        if (e is null) return null;
        if (!await access.CanReadCalendarAsync(userId, e.CalendarId, ct)) throw new AccessDeniedException();
        return ToDto(e);
    }

    public async Task<EventDto> UpdateAsync(Guid userId, Guid id, UpdateEventRequest r, CancellationToken ct = default)
    {
        var e = await db.Events.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException("Event not found.");
        if (!await access.CanWriteCalendarAsync(userId, e.CalendarId, ct)) throw new AccessDeniedException();

        if (r.Title is not null) e.Title = r.Title;
        if (r.Description is not null) e.Description = r.Description;
        if (r.Location is not null) e.Location = r.Location;
        if (r.Status is not null) e.Status = r.Status;
        if (r.StartsAt is not null) e.StartsAt = r.StartsAt;
        if (r.EndsAt is not null) e.EndsAt = r.EndsAt;
        if (r.RecurrenceRule is not null) e.RecurrenceRule = r.RecurrenceRule;
        if (r.Tags is not null) e.Tags = r.Tags;

        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.SourceIcalendar = ICalSerializer.ToICalendar(e);
        e.ContentHash = ContentHash.Of(e.SourceIcalendar);
        await BumpAndLogAsync(e.CalendarId, e.IcalUid, "saved", e.ContentHash, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var e = await db.Events.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException("Event not found.");
        if (!await access.CanWriteCalendarAsync(userId, e.CalendarId, ct)) throw new AccessDeniedException();

        e.DeletedAt = DateTimeOffset.UtcNow;
        await BumpAndLogAsync(e.CalendarId, e.IcalUid, "deleted", null, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<EventDto> AttachMetadataAsync(Guid userId, Guid id, JsonNode patch, CancellationToken ct = default)
    {
        var e = await db.Events.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException("Event not found.");
        if (!await access.CanWriteCalendarAsync(userId, e.CalendarId, ct)) throw new AccessDeniedException();

        var current = (JsonNode.Parse(string.IsNullOrWhiteSpace(e.Metadata) ? "{}" : e.Metadata) as JsonObject) ?? new JsonObject();
        if (patch is JsonObject obj)
            foreach (var kv in obj)
                current[kv.Key] = kv.Value?.DeepClone();

        e.Metadata = current.ToJsonString();
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    private async Task BumpAndLogAsync(Guid calendarId, string icalUid, string changeType, string? hash, CancellationToken ct)
    {
        var cal = await db.Calendars.FirstAsync(c => c.Id == calendarId, ct);
        cal.Revision += 1;
        cal.UpdatedAt = DateTimeOffset.UtcNow;
        db.CalendarChanges.Add(new CalendarChange
        {
            CalendarId = calendarId, Revision = cal.Revision,
            ItemIcalUid = icalUid, ChangeType = changeType, ContentHash = hash,
        });
    }

    private static DateTimeOffset? OccurrenceStart(Event e)
    {
        if (e.IsAllDay && e.StartDate is { } d) return new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero);
        return e.StartsAt;
    }

    public static EventDto ToDto(Event e) => new(
        e.Id, e.CalendarId, e.IcalUid, e.Title, e.Description, e.Location, e.Status, e.IsAllDay,
        e.StartsAt, e.EndsAt, e.StartDate, e.EndDate, e.RecurrenceRule, e.Tags,
        JsonNode.Parse(string.IsNullOrWhiteSpace(e.Metadata) ? "{}" : e.Metadata), e.ContentHash);
}
