using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Mappers;
using LupiraCalApi.Serialization;
using Marten;
using System.Text.Json.Nodes;

namespace LupiraCalApi.Application;

/// <summary>
/// The calendar-item core shared by REST, DAV, and MCP. Every mutation appends events to the item's Marten stream;
/// the inline <see cref="CalendarItem"/> snapshot is the read model. The raw <c>SourceIcalendar</c> blob + content
/// hash ride on the events (DAV source of truth + ETag). Items are calendar-independent (many-to-many via curation).
/// </summary>
public sealed class CalendarItemService(IDocumentSession session, AccessResolver access, RecurrenceExpander expander, PlaceService places)
{
    public async Task<OpResult<CalendarItemDto>> CreateAsync(Guid principalId, CreateCalendarItemRequest r, CancellationToken ct = default)
    {
        if (r.CalendarId is { } pre && !await access.CanWriteCalendarAsync(principalId, pre, ct))
            return OpResult<CalendarItemDto>.Forbidden("No write access to this calendar.");

        var placeId = await places.ResolveLabelAsync(r.Location, ct);
        var status = ParseStatus(r.Status);
        var kind = ParseKind(r.Kind);
        var uid = $"{Guid.NewGuid():N}@cal.lupira.com";
        var id = DeterministicGuid.From(uid);
        var fields = new CalendarItemFields(r.Title, r.Description, status, r.IsAllDay, r.StartsAt, r.EndsAt,
            r.StartTimezone, null, r.StartDate, r.EndDate, r.RecurrenceRule, kind, placeId, null, r.Tags);
        var ics = ICalSerializer.ToICalendar(uid, r.Title, r.Description, r.Location, status, r.IsAllDay, r.StartsAt, r.EndsAt, r.StartDate, r.EndDate, r.RecurrenceRule);
        var hash = ContentHash.Of(ics);

        var events = new List<object> { new ItemScheduled(id, uid, fields, null, ics, hash) };
        if (r.CalendarId is { } calId)
            events.Add(new AddedToCalendar(id, calId, CalendarEntryStatus.Accepted, DateTimeOffset.UtcNow));

        session.Events.StartStream<CalendarItem>(id, events.ToArray());
        await session.SaveChangesAsync(ct);
        var item = await session.LoadAsync<CalendarItem>(id, ct);
        return OpResult<CalendarItemDto>.Ok(item!.ToResponse());
    }

    public async Task<OpResult<List<CalendarItemOccurrenceDto>>> SearchAsync(
        Guid principalId, string? query, DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId, string? tag, CancellationToken ct = default)
    {
        var calIds = await access.AccessibleCalendarIdsAsync(principalId, ct);
        if (calendarId is { } cid)
        {
            if (!calIds.Contains(cid)) return OpResult<List<CalendarItemOccurrenceDto>>.Forbidden("No access to this calendar.");
            calIds = [cid];
        }

        // Personal/family scale: load live items, filter membership + text in memory (the snapshot is the read model).
        var candidates = await session.Query<CalendarItem>().Where(i => i.DeletedAt == null).ToListAsync(ct);
        IEnumerable<CalendarItem> items = candidates.Where(i =>
            i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && calIds.Contains(m.CalendarId)));
        if (!string.IsNullOrWhiteSpace(tag)) items = items.Where(i => i.Tags is not null && i.Tags.Contains(tag));
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            items = items.Where(i =>
                (i.Title ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (i.Description ?? "").Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var windowStart = from ?? DateTimeOffset.UtcNow.AddYears(-1);
        var windowEnd = to ?? DateTimeOffset.UtcNow.AddYears(1);
        var results = new List<CalendarItemOccurrenceDto>();
        foreach (var i in items)
        {
            TimeSpan? duration = (i.StartsAt is { } s && i.EndsAt is { } en) ? en - s : null;
            if (!string.IsNullOrWhiteSpace(i.RecurrenceRule))
            {
                foreach (var occ in expander.Expand(i.SourceIcalendar, windowStart, windowEnd))
                    results.Add(new CalendarItemOccurrenceDto(i.Id, i.Title, i.PlaceId, i.IsAllDay, occ, duration is { } d ? occ + d : null, i.ContentHash));
            }
            else if (OccurrenceStart(i) is { } start && start >= windowStart && start < windowEnd)
            {
                results.Add(new CalendarItemOccurrenceDto(i.Id, i.Title, i.PlaceId, i.IsAllDay, start, duration is { } d ? start + d : i.EndsAt, i.ContentHash));
            }
        }
        return OpResult<List<CalendarItemOccurrenceDto>>.Ok([.. results.OrderBy(x => x.Start)]);
    }

    public async Task<OpResult<CalendarItemDto>> GetAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var item = await session.LoadAsync<CalendarItem>(id, ct);
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        var calIds = await access.AccessibleCalendarIdsAsync(principalId, ct);
        if (!item.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && calIds.Contains(m.CalendarId)))
            return OpResult<CalendarItemDto>.Forbidden("No access to this item.");
        return OpResult<CalendarItemDto>.Ok(item.ToResponse());
    }

    public async Task<OpResult<CalendarItemDto>> UpdateAsync(Guid principalId, Guid id, UpdateCalendarItemRequest r, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult<CalendarItemDto>.Forbidden("No write access to this item.");

        var title = r.Title ?? item.Title;
        var description = r.Description ?? item.Description;
        var status = r.Status is not null ? ParseStatus(r.Status) : item.Status;
        var startsAt = r.StartsAt ?? item.StartsAt;
        var endsAt = r.EndsAt ?? item.EndsAt;
        var rrule = r.RecurrenceRule ?? item.RecurrenceRule;
        var tags = r.Tags ?? item.Tags;
        var placeId = r.Location is not null ? await places.ResolveLabelAsync(r.Location, ct) : item.PlaceId;

        var fields = new CalendarItemFields(title, description, status, item.IsAllDay, startsAt, endsAt,
            item.StartTimezone, item.EndTimezone, item.StartDate, item.EndDate, rrule, item.Kind, placeId, item.ParentItemId, tags);
        var locationLabel = await places.LabelOfAsync(placeId, ct);
        var ics = ICalSerializer.ToICalendar(item.IcalUid, title, description, locationLabel, status, item.IsAllDay, startsAt, endsAt, item.StartDate, item.EndDate, rrule);
        var hash = ContentHash.Of(ics);

        stream.AppendOne(new ItemRevised(id, fields, null, ics, hash));
        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<CalendarItem>(id, ct);
        return OpResult<CalendarItemDto>.Ok(updated!.ToResponse());
    }

    public async Task<OpResult> DeleteAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult.Forbidden("No write access to this item.");
        stream.AppendOne(new ItemDeleted(id));
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    public async Task<OpResult<CalendarItemDto>> AttachMetadataAsync(Guid principalId, Guid id, JsonNode patch, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult<CalendarItemDto>.Forbidden("No write access to this item.");

        var current = (JsonNode.Parse(string.IsNullOrWhiteSpace(item.Metadata) ? "{}" : item.Metadata) as JsonObject) ?? new JsonObject();
        if (patch is JsonObject obj)
            foreach (var kv in obj) current[kv.Key] = kv.Value?.DeepClone();
        stream.AppendOne(new ItemMetadataAttached(id, current.ToJsonString()));
        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<CalendarItem>(id, ct);
        return OpResult<CalendarItemDto>.Ok(updated!.ToResponse());
    }

    // ---- DAV write path: upsert/delete a resource keyed by its URL uid (and the calendar it's addressed under) ----

    public async Task<OpResult<DavWriteResult>> PutIcsAsync(
        Guid principalId, Guid calendarId, string icalUid, string rawIcs, string? ifMatch, bool ifNoneMatchStar, CancellationToken ct = default)
    {
        if (!await access.CanWriteCalendarAsync(principalId, calendarId, ct)) return OpResult<DavWriteResult>.Forbidden("No write access to this calendar.");

        var id = DeterministicGuid.From(icalUid);
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var existing = stream.Aggregate;
        // Streams are keyed by iCal UID alone, so a UID can resolve to another principal's item. Only allow writing
        // an item already associated with this calendar (incl. resurrecting one removed from it), an unclaimed item
        // (no accepted membership anywhere), or one the caller can already write — never overwrite a foreign item.
        var mayWrite = existing is null
            || existing.Calendars.Any(m => m.CalendarId == calendarId)
            || !existing.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted)
            || await CanWriteItemAsync(principalId, existing, ct);
        if (!mayWrite) return OpResult<DavWriteResult>.Forbidden("This resource belongs to another collection.");
        var liveInCal = existing is { DeletedAt: null } && existing.IsAcceptedIn(calendarId);
        if (ifNoneMatchStar && liveInCal) return OpResult<DavWriteResult>.Conflict("Resource already exists.");
        if (ifMatch is not null && (!liveInCal || existing!.ContentHash != ifMatch)) return OpResult<DavWriteResult>.Conflict("ETag mismatch.");

        ParsedEvent p;
        try { p = ICalSerializer.ParseICalendar(rawIcs); }
        catch (FormatException ex) { return OpResult<DavWriteResult>.Invalid(ex.Message); }

        var placeId = await places.ResolveLabelAsync(p.Location, ct);
        var fields = new CalendarItemFields(p.Title, p.Description, null, p.IsAllDay, p.StartsAt, p.EndsAt,
            p.StartTimezone, p.EndTimezone, p.StartDate, p.EndDate, p.RecurrenceRule, null, placeId, null, null);
        var hash = ContentHash.Of(rawIcs);

        stream.AppendOne(new ItemIcsPut(id, icalUid, fields, rawIcs, hash));   // also clears any soft-delete (resurrect)
        if (existing is null || !existing.IsAcceptedIn(calendarId))
            stream.AppendOne(new AddedToCalendar(id, calendarId, CalendarEntryStatus.Accepted, DateTimeOffset.UtcNow));

        await session.SaveChangesAsync(ct);
        return OpResult<DavWriteResult>.Ok(new DavWriteResult(!liveInCal, hash));
    }

    public async Task<OpResult> DeleteByUidAsync(Guid principalId, Guid calendarId, string icalUid, string? ifMatch, CancellationToken ct = default)
    {
        if (!await access.CanWriteCalendarAsync(principalId, calendarId, ct)) return OpResult.Forbidden("No write access to this calendar.");

        var id = DeterministicGuid.From(icalUid);
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null || !item.IsAcceptedIn(calendarId)) return OpResult.NotFound();
        if (ifMatch is not null && item.ContentHash != ifMatch) return OpResult.Conflict("ETag mismatch.");

        // DAV DELETE removes the resource from THIS calendar (the item lives on, unfiled, if it was only here).
        stream.AppendOne(new RemovedFromCalendar(id, calendarId, DateTimeOffset.UtcNow));
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    private async Task<bool> CanWriteItemAsync(Guid principalId, CalendarItem item, CancellationToken ct)
    {
        foreach (var m in item.Calendars.Where(x => x.Status == CalendarEntryStatus.Accepted))
            if (await access.CanWriteCalendarAsync(principalId, m.CalendarId, ct)) return true;
        return false;
    }

    private static DateTimeOffset? OccurrenceStart(CalendarItem i)
    {
        if (i.IsAllDay && i.StartDate is { } d) return new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero);
        return i.StartsAt;
    }

    private static ItemStatus? ParseStatus(string? s) => Enum.TryParse<ItemStatus>(s, ignoreCase: true, out var v) ? v : null;
    private static ItemKind? ParseKind(string? s) => Enum.TryParse<ItemKind>(s, ignoreCase: true, out var v) ? v : null;
}
