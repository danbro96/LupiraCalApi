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
public sealed class CalendarItemService(IDocumentSession session, AccessResolver access, RecurrenceExpander expander, IGeoResolver geo, CompletenessResolver completeness)
{
    /// <summary>Resolve free-text to a (geo place id, label). Geo owns resolution. <c>Unresolved</c> is true only when geo
    /// IS configured but couldn't resolve (unreachable/GeocodeUnavailable) — a retryable failure the REST/MCP paths reject
    /// (fail-closed) while the DAV path ignores (label-only). When geo is unconfigured (dev/test) it degrades to the
    /// trimmed-text label and is never treated as an error.</summary>
    private async Task<(Guid? PlaceId, string? Label, bool Unresolved)> ResolvePlaceAsync(string? text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null, false);
        if (!geo.IsConfigured) return (null, text.Trim(), false);
        if (await geo.ResolveAsync(text, ct) is { } r) return (r.PlaceId, r.Name, false);
        return (null, text.Trim(), true);
    }

    private const string LocationUnresolved = "Location could not be resolved to a place (geo unavailable) — retry.";
    private const string TravelUnresolved = "A travel location could not be resolved to a place (geo unavailable) — retry.";

    public async Task<OpResult<CalendarItemDto>> CreateAsync(Guid principalId, CreateCalendarItemRequest r, CancellationToken ct = default)
    {
        if (r.CalendarId is { } pre && !await access.CanWriteCalendarAsync(principalId, pre, ct))
            return OpResult<CalendarItemDto>.Forbidden("No write access to this calendar.");

        var (placeId, locationLabel, unresolved) = await ResolvePlaceAsync(r.Location, ct);
        if (unresolved) return OpResult<CalendarItemDto>.Invalid(LocationUnresolved);
        if (!TryParseDefined<ItemStatus>(r.Status, out var status)) return OpResult<CalendarItemDto>.Invalid(UnknownEnum<ItemStatus>("status", r.Status!));
        if (!TryParseDefined<ItemCategory>(r.Category, out var category)) return OpResult<CalendarItemDto>.Invalid(UnknownEnum<ItemCategory>("category", r.Category!));
        if (ItemDetailsMapper.Validate(category, r.Details) is { } detailsError)
            return OpResult<CalendarItemDto>.Invalid(detailsError);
        var (details, detailsUnresolved) = await ItemDetailsMapper.BuildAsync(r.Details, r.Availability, geo, ct);
        if (detailsUnresolved) return OpResult<CalendarItemDto>.Invalid(TravelUnresolved);
        var uid = $"{Guid.NewGuid():N}@cal.lupira.com";
        var id = DeterministicGuid.From(uid);
        var fields = new CalendarItemFields(r.Title, r.Description, status, r.IsAllDay, r.StartsAt, r.EndsAt,
            r.StartTimezone, null, r.StartDate, r.EndDate, r.RecurrenceRule, null, null, category, placeId, locationLabel, null, r.Tags,
            r.StartPrecision, r.EndPrecision);

        var events = new List<object> { new ItemScheduled(id, uid, fields, details) };
        if (r.Metadata is { } meta)
            events.Add(new ItemMetadataAttached(id, meta.ToJsonString()));
        if (r.CalendarId is { } calId)
            events.Add(new AddedToCalendar(id, calId, CalendarEntryStatus.Accepted, DateTimeOffset.UtcNow));

        session.Events.StartStream<CalendarItem>(id, events.ToArray());
        await session.SaveChangesAsync(ct);
        var item = await session.LoadAsync<CalendarItem>(id, ct);
        return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(item!, ct));
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

        var itemList = items.ToList();
        var scores = await completeness.ScoreItemsAsync(itemList, ct);

        var windowStart = from ?? DateTimeOffset.UtcNow.AddYears(-1);
        var windowEnd = to ?? DateTimeOffset.UtcNow.AddYears(1);
        var results = new List<CalendarItemOccurrenceDto>();
        foreach (var i in itemList)
        {
            var score = scores[i.Id];
            TimeSpan? duration = (i.StartsAt is { } s && i.EndsAt is { } en) ? en - s : null;
            if (!string.IsNullOrWhiteSpace(i.RecurrenceRule))
            {
                foreach (var occ in expander.Expand(i, windowStart, windowEnd))
                    results.Add(new CalendarItemOccurrenceDto { Id = i.Id, Title = i.Title, PlaceId = i.PlaceId, LocationLabel = i.LocationLabel, IsAllDay = i.IsAllDay, Start = occ, End = duration is { } d ? occ + d : null, Completeness = score, Etag = i.ContentHash });
            }
            else if (OccurrenceStart(i) is { } start && start >= windowStart && start < windowEnd)
            {
                results.Add(new CalendarItemOccurrenceDto { Id = i.Id, Title = i.Title, PlaceId = i.PlaceId, LocationLabel = i.LocationLabel, IsAllDay = i.IsAllDay, Start = start, End = duration is { } d ? start + d : i.EndsAt, Completeness = score, Etag = i.ContentHash });
            }
        }
        return OpResult<List<CalendarItemOccurrenceDto>>.Ok([.. results.OrderBy(x => x.Start)]);
    }

    /// <summary>Reverse index: items anchored to a place — as their location (<c>PlaceId</c>) or a travel/car endpoint.
    /// Full items (not recurrence-expanded), scoped to calendars the principal can read.</summary>
    public async Task<OpResult<List<CalendarItemDto>>> ByPlaceAsync(Guid principalId, Guid placeId, CancellationToken ct = default)
    {
        var calIds = await access.AccessibleCalendarIdsAsync(principalId, ct);
        var candidates = await session.Query<CalendarItem>().Where(i => i.DeletedAt == null).ToListAsync(ct);
        var items = candidates
            .Where(i => i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && calIds.Contains(m.CalendarId))
                && ReferencesPlace(i, placeId))
            .OrderBy(i => i.StartsAt).ThenBy(i => i.Title)
            .ToList();
        var scores = await completeness.ScoreItemsAsync(items, ct);
        return OpResult<List<CalendarItemDto>>.Ok(items.Select(i => i.ToResponse(scores[i.Id])).ToList());
    }

    private static bool ReferencesPlace(CalendarItem i, Guid placeId) =>
        i.PlaceId == placeId
        || (i.Details?.Travel is { } t && (t.ToPlaceId == placeId || t.FromPlaceId == placeId));

    public async Task<OpResult<CalendarItemDto>> GetAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var item = await session.LoadAsync<CalendarItem>(id, ct);
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        var calIds = await access.AccessibleCalendarIdsAsync(principalId, ct);
        if (!item.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && calIds.Contains(m.CalendarId)))
            return OpResult<CalendarItemDto>.Forbidden("No access to this item.");
        return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(item, ct));
    }

    public async Task<OpResult<CalendarItemDto>> UpdateAsync(Guid principalId, Guid id, UpdateCalendarItemRequest r, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult<CalendarItemDto>.Forbidden("No write access to this item.");

        if (!TryParseDefined<ItemStatus>(r.Status, out var statusIn)) return OpResult<CalendarItemDto>.Invalid(UnknownEnum<ItemStatus>("status", r.Status!));
        if (!TryParseDefined<ItemCategory>(r.Category, out var categoryIn)) return OpResult<CalendarItemDto>.Invalid(UnknownEnum<ItemCategory>("category", r.Category!));

        var title = r.Title ?? item.Title;
        var description = r.Description ?? item.Description;
        var status = statusIn ?? item.Status;
        var startsAt = r.StartsAt ?? item.StartsAt;
        var endsAt = r.EndsAt ?? item.EndsAt;
        var rrule = r.RecurrenceRule ?? item.RecurrenceRule;
        var tags = r.Tags ?? item.Tags;
        var (placeId, locationLabel, unresolved) = r.Location is not null ? await ResolvePlaceAsync(r.Location, ct) : (item.PlaceId, item.LocationLabel, false);
        if (unresolved) return OpResult<CalendarItemDto>.Invalid(LocationUnresolved);

        var category = categoryIn ?? item.Category;
        var categoryChanged = category != item.Category;

        // Validate against the resolved category (an omitted r.Category means "the item's current category").
        if (ItemDetailsMapper.Validate(category, r.Details) is { } detailsError)
            return OpResult<CalendarItemDto>.Invalid(detailsError);

        var fields = new CalendarItemFields(title, description, status, item.IsAllDay, startsAt, endsAt,
            item.StartTimezone, item.EndTimezone, item.StartDate, item.EndDate, rrule,
            item.RecurrenceExceptions, item.RecurrenceOverrides, category, placeId, locationLabel, item.ParentItemId, tags,
            r.StartPrecision ?? item.StartPrecision, r.EndPrecision ?? item.EndPrecision);

        var (incoming, detailsUnresolved) = await ItemDetailsMapper.BuildAsync(r.Details, r.Availability, geo, ct);
        if (detailsUnresolved) return OpResult<CalendarItemDto>.Invalid(TravelUnresolved);
        // null incoming keeps existing details (Apply only overwrites when non-null); reclassifying with none clears the previous details.
        var details = incoming is null
            ? (categoryChanged ? new ItemDetails() : null)
            : (categoryChanged ? incoming : ItemDetailsMapper.Merge(item.Details, incoming));

        stream.AppendOne(new ItemRevised(id, fields, details));
        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<CalendarItem>(id, ct);
        return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(updated!, ct));
    }

    public async Task<OpResult> DeleteAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult.Forbidden("No write access to this item.");
        stream.AppendOne(new ItemDeleted(id, DateTimeOffset.UtcNow));
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
        return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(updated!, ct));
    }

    // ---- event-bound payload (server-side only, XOR — an item carries one prompt OR one action) ----

    public async Task<OpResult<CalendarItemDto>> SetPromptAsync(Guid principalId, Guid id, ItemPrompt prompt, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult<CalendarItemDto>.Forbidden("No write access to this item.");
        if (item.Action is not null) return OpResult<CalendarItemDto>.Conflict("Item already carries an action; clear it first.");

        stream.AppendOne(new ItemPromptSet(id, prompt));
        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<CalendarItem>(id, ct);
        return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(updated!, ct));
    }

    public async Task<OpResult<CalendarItemDto>> SetActionAsync(Guid principalId, Guid id, ItemAction action, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult<CalendarItemDto>.Forbidden("No write access to this item.");
        if (item.Prompt is not null) return OpResult<CalendarItemDto>.Conflict("Item already carries a prompt; clear it first.");

        stream.AppendOne(new ItemActionSet(id, action));
        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<CalendarItem>(id, ct);
        return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(updated!, ct));
    }

    public async Task<OpResult> ClearPromptAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult.Forbidden("No write access to this item.");
        if (item.Prompt is null) return OpResult.Ok();   // no-op; don't append a meaningless event

        stream.AppendOne(new ItemPromptCleared(id));
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    public async Task<OpResult> ClearActionAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult.Forbidden("No write access to this item.");
        if (item.Action is null) return OpResult.Ok();

        stream.AppendOne(new ItemActionCleared(id));
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    // ---- DAV write path: upsert/delete a resource keyed by its URL uid (and the calendar it's addressed under) ----

    public async Task<OpResult<DavWriteResult>> PutIcsAsync(
        Guid principalId, Guid calendarId, string externalId, string rawIcs, string? ifMatch, bool ifNoneMatchStar, CancellationToken ct = default)
    {
        if (!await access.CanWriteCalendarAsync(principalId, calendarId, ct)) return OpResult<DavWriteResult>.Forbidden("No write access to this calendar.");

        var id = DeterministicGuid.From(externalId);
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

        var (placeId, locationLabel, _) = await ResolvePlaceAsync(p.Location, ct);   // DAV stays lenient: external clients send free-text, unresolved → label-only
        var fields = new CalendarItemFields(p.Title, p.Description, null, p.IsAllDay, p.StartsAt, p.EndsAt,
            p.StartTimezone, p.EndTimezone, p.StartDate, p.EndDate, p.RecurrenceRule,
            p.RecurrenceExceptions, p.RecurrenceOverrides, null, placeId, locationLabel, null, null);

        stream.AppendOne(new ItemImported(id, externalId, fields));   // also clears any soft-delete (resurrect)
        if (existing is null || !existing.IsAcceptedIn(calendarId))
            stream.AppendOne(new AddedToCalendar(id, calendarId, CalendarEntryStatus.Accepted, DateTimeOffset.UtcNow));

        await session.SaveChangesAsync(ct);
        // The ETag is derived in the snapshot from the canonical ICS; read it back rather than recomputing it here.
        var saved = await session.LoadAsync<CalendarItem>(id, ct);
        return OpResult<DavWriteResult>.Ok(new DavWriteResult(!liveInCal, saved!.ContentHash));
    }

    public async Task<OpResult> DeleteByUidAsync(Guid principalId, Guid calendarId, string externalId, string? ifMatch, CancellationToken ct = default)
    {
        if (!await access.CanWriteCalendarAsync(principalId, calendarId, ct)) return OpResult.Forbidden("No write access to this calendar.");

        var id = DeterministicGuid.From(externalId);
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

    private async Task<CalendarItemDto> ToDtoAsync(CalendarItem item, CancellationToken ct) =>
        item.ToResponse(await completeness.ScoreItemAsync(item, ct));

    /// <summary>Parses an optional enum name. False only for a non-null value that isn't a defined name —
    /// undefined numeric strings ("99") are rejected, not persisted as out-of-range values.</summary>
    private static bool TryParseDefined<TEnum>(string? value, out TEnum? parsed) where TEnum : struct, Enum
    {
        parsed = null;
        if (value is null) return true;
        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var v) || !Enum.IsDefined(v)) return false;
        parsed = v;
        return true;
    }

    private static string UnknownEnum<TEnum>(string field, string value) where TEnum : struct, Enum =>
        $"Unknown {field} '{value}'. Valid values: {string.Join(", ", Enum.GetNames<TEnum>())}.";
}
