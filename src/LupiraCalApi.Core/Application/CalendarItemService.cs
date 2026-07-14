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
    private const string LocationNeedsPlaceId = "Resolve the location to a LupiraGeoApi place first and pass PlaceId — free-text Location is accepted only over CalDAV.";

    public async Task<OpResult<CalendarItemDto>> CreateAsync(Guid principalId, CreateCalendarItemRequest r, CancellationToken ct = default)
    {
        if (r.CalendarId is { } pre && !await access.CanWriteCalendarAsync(principalId, pre, ct))
            return OpResult<CalendarItemDto>.Forbidden("No write access to this calendar.");

        // A client SourceKey pins the stream id + external UID (idempotent import replay); else a random uid.
        var hasKey = !string.IsNullOrWhiteSpace(r.SourceKey);
        var uid = hasKey ? r.SourceKey!.Trim() : $"{Guid.NewGuid():N}@cal.lupira.com";
        var id = DeterministicGuid.From(uid);
        var stream = hasKey ? await session.Events.FetchForWriting<CalendarItem>(id, ct) : null;
        if (stream?.Aggregate is { DeletedAt: null } live)
            return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(live, ct));   // idempotent hit — no new events

        // REST/MCP require a pre-resolved place: pass PlaceId (Location, if any, is only the display label).
        // Free-text resolution lives on the CalDAV path (PutIcsAsync), where external clients can't send a place id.
        Guid? placeId;
        string? locationLabel;
        if (r.PlaceId is { } pid)
        {
            placeId = pid;
            locationLabel = string.IsNullOrWhiteSpace(r.Location) ? null : r.Location.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(r.Location))
        {
            return OpResult<CalendarItemDto>.Invalid(LocationNeedsPlaceId);
        }
        else
        {
            placeId = null;
            locationLabel = null;
        }

        if (!TryParseDefined<ItemStatus>(r.Status, out var status)) return OpResult<CalendarItemDto>.Invalid(UnknownEnum<ItemStatus>("status", r.Status!));
        if (!TryParseDefined<ItemCategory>(r.Category, out var category)) return OpResult<CalendarItemDto>.Invalid(UnknownEnum<ItemCategory>("category", r.Category!));
        if (ItemDetailsMapper.Validate(category, r.Details) is { } detailsError)
            return OpResult<CalendarItemDto>.Invalid(detailsError);
        var (details, detailsUnresolved) = await ItemDetailsMapper.BuildAsync(r.Details, r.Availability, geo, ct);
        if (detailsUnresolved) return OpResult<CalendarItemDto>.Invalid(TravelUnresolved);

        // Parent by explicit id, else by the parent's SourceKey (batch imports resolve it deterministically).
        var parentItemId = r.ParentItemId ?? (string.IsNullOrWhiteSpace(r.ParentSourceKey) ? (Guid?)null : DeterministicGuid.From(r.ParentSourceKey!.Trim()));
        if (await ParentInvalidAsync(principalId, parentItemId, ct) is { } parentError)
            return OpResult<CalendarItemDto>.Invalid(parentError);

        var fields = new CalendarItemFields(r.Title, r.Description, status, r.IsAllDay, r.StartsAt, r.EndsAt,
            r.StartTimezone, null, r.StartDate, r.EndDate, r.RecurrenceRule, null, null, category, placeId, locationLabel, parentItemId, r.Tags,
            r.StartPrecision, r.EndPrecision);

        var events = new List<object> { new ItemScheduled(id, uid, fields, details) };
        if (r.Metadata is { } meta)
            events.Add(new ItemMetadataAttached(id, meta.ToJsonString()));
        if (r.CalendarId is { } calId)
            events.Add(new AddedToCalendar(id, calId, CalendarEntryStatus.Accepted, DateTimeOffset.UtcNow));

        if (stream is not null)
            foreach (var e in events) stream.AppendOne(e);
        else
            session.Events.StartStream<CalendarItem>(id, events.ToArray());
        await session.SaveChangesAsync(ct);
        var item = await session.LoadAsync<CalendarItem>(id, ct);
        return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(item!, ct));
    }

    public const int MaxBatch = 500;

    /// <summary>Create many items in one call. Topologically orders by in-batch <c>ParentSourceKey</c> so parents persist
    /// before children (each item saves via <see cref="CreateAsync"/>, which is idempotent on <c>SourceKey</c>). Never aborts
    /// the whole batch on one bad item — returns a per-item status (created | existed | invalid). Results are in input order.</summary>
    public async Task<OpResult<List<ItemBatchResult>>> CreateBatchAsync(Guid principalId, IReadOnlyList<CreateCalendarItemRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0) return OpResult<List<ItemBatchResult>>.Invalid("At least one item is required.");
        if (requests.Count > MaxBatch) return OpResult<List<ItemBatchResult>>.Invalid($"At most {MaxBatch} items per batch.");

        var inBatchKeys = new HashSet<string>(requests.Where(r => !string.IsNullOrWhiteSpace(r.SourceKey)).Select(r => r.SourceKey!.Trim()));
        var ordered = TopoOrderByParent(requests, inBatchKeys);

        var byRequest = new Dictionary<CreateCalendarItemRequest, ItemBatchResult>(ReferenceEqualityComparer.Instance);
        foreach (var r in ordered)
        {
            if (!string.IsNullOrWhiteSpace(r.SourceKey)
                && await session.LoadAsync<CalendarItem>(DeterministicGuid.From(r.SourceKey!.Trim()), ct) is { DeletedAt: null } existing)
            {
                byRequest[r] = new ItemBatchResult(r.SourceKey, existing.Id, "existed", null);
                continue;
            }
            var res = await CreateAsync(principalId, r, ct);
            byRequest[r] = res.Status == OpStatus.Ok
                ? new ItemBatchResult(r.SourceKey, res.Value!.Id, "created", null)
                : new ItemBatchResult(r.SourceKey, null, "invalid", res.Error);
        }
        return OpResult<List<ItemBatchResult>>.Ok([.. requests.Select(r => byRequest[r])]);
    }

    // Parents (referenced by another item's ParentSourceKey) before their children. Items whose parent is not in the
    // batch keep their relative order. A cycle (shouldn't happen) drops the remainder in place — they fail parent validation.
    private static List<CreateCalendarItemRequest> TopoOrderByParent(IReadOnlyList<CreateCalendarItemRequest> requests, HashSet<string> inBatchKeys)
    {
        var pending = new List<CreateCalendarItemRequest>(requests);
        var emitted = new HashSet<string>();
        var order = new List<CreateCalendarItemRequest>(requests.Count);
        while (pending.Count > 0)
        {
            var progressed = false;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var r = pending[i];
                var psk = r.ParentSourceKey?.Trim();
                var blocked = r.ParentItemId is null && !string.IsNullOrWhiteSpace(psk) && inBatchKeys.Contains(psk!) && !emitted.Contains(psk!);
                if (blocked) continue;
                pending.RemoveAt(i);
                order.Add(r);
                if (!string.IsNullOrWhiteSpace(r.SourceKey)) emitted.Add(r.SourceKey!.Trim());
                progressed = true;
            }
            if (!progressed) { order.AddRange(pending); break; }
        }
        return order;
    }

    public async Task<OpResult<List<CalendarItemOccurrenceDto>>> SearchAsync(
        Guid principalId, string? query, DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId, string? tag, Guid? parentId,
        string? category = null, string? status = null, int? skip = null, int? take = null, bool desc = false, CancellationToken ct = default)
    {
        if (!TryParseDefined<ItemCategory>(category, out var cat))
            return OpResult<List<CalendarItemOccurrenceDto>>.Invalid(UnknownEnum<ItemCategory>("category", category!));
        if (!TryParseDefined<ItemStatus>(status, out var st))
            return OpResult<List<CalendarItemOccurrenceDto>>.Invalid(UnknownEnum<ItemStatus>("status", status!));
        if (skip is < 0) return OpResult<List<CalendarItemOccurrenceDto>>.Invalid("skip must be >= 0.");
        if (take is < 1) return OpResult<List<CalendarItemOccurrenceDto>>.Invalid("take must be >= 1.");

        // accessibleIds enriches occurrences with every readable membership; searchIds (possibly narrowed) scopes the match.
        var accessibleIds = await access.AccessibleCalendarIdsAsync(principalId, ct);
        var searchIds = accessibleIds;
        if (calendarId is { } cid)
        {
            if (!accessibleIds.Contains(cid)) return OpResult<List<CalendarItemOccurrenceDto>>.Forbidden("No access to this calendar.");
            searchIds = [cid];
        }

        // Personal/family scale: load live items, filter membership + text in memory (the snapshot is the read model).
        var candidates = await session.Query<CalendarItem>().Where(i => i.DeletedAt == null).ToListAsync(ct);

        // Hierarchy enrichment is gated on readability: candidates spans every principal's items,
        // so an unreadable parent must yield a null title and unreadable children must not count.
        bool Readable(CalendarItem c) => c.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && accessibleIds.Contains(m.CalendarId));
        var titleById = candidates.Where(Readable).ToDictionary(c => c.Id, c => c.Title);
        var childCounts = candidates.Where(c => c.ParentItemId is not null && Readable(c))
            .GroupBy(c => c.ParentItemId!.Value).ToDictionary(g => g.Key, g => g.Count());

        IEnumerable<CalendarItem> items = candidates.Where(i =>
            i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && searchIds.Contains(m.CalendarId)));
        if (!string.IsNullOrWhiteSpace(tag)) items = items.Where(i => i.Tags is not null && i.Tags.Contains(tag));
        if (parentId is { } parent) items = items.Where(i => i.ParentItemId == parent);
        if (cat is not null) items = items.Where(i => i.Category == cat);
        if (st is not null) items = items.Where(i => i.Status == st);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            items = items.Where(i =>
                (i.Title ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (i.Description ?? "").Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var itemList = items.ToList();
        var scores = await completeness.ScoreItemsAsync(itemList, ct);

        // Text queries and parent drill-downs default to all-time; browsing keeps the ±1y window.
        var allTime = !string.IsNullOrWhiteSpace(query) || parentId is not null;
        var windowStart = from ?? (allTime ? DateTimeOffset.MinValue : DateTimeOffset.UtcNow.AddYears(-1));
        var windowEnd = to ?? (allTime ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddYears(1));
        // RRULE expansion needs a finite ceiling — an unbounded rule against MaxValue never terminates.
        var expansionEnd = to ?? DateTimeOffset.UtcNow.AddYears(1);
        var results = new List<CalendarItemOccurrenceDto>();
        foreach (var i in itemList)
        {
            var score = scores[i.Id];
            var memberIds = i.Calendars
                .Where(m => m.Status == CalendarEntryStatus.Accepted && accessibleIds.Contains(m.CalendarId))
                .Select(m => m.CalendarId).ToArray();
            var parentTitle = i.ParentItemId is { } pid ? titleById.GetValueOrDefault(pid) : null;
            var childCount = childCounts.GetValueOrDefault(i.Id);
            // All-day items carry their span in StartDate/EndDate, not StartsAt/EndsAt; derive the end at the
            // inclusive last day's 00:00Z (same convention as TimeRangeFilter) so multi-day all-day occurrences
            // report an End instead of null.
            TimeSpan? duration =
                i.StartsAt is { } s && i.EndsAt is { } en ? en - s
                : i.IsAllDay && i.StartDate is { } sd && i.EndDate is { } ed ? AllDayInstant(ed) - AllDayInstant(sd)
                : null;
            if (!string.IsNullOrWhiteSpace(i.RecurrenceRule))
            {
                foreach (var occ in expander.Expand(i, windowStart, expansionEnd))
                    results.Add(new CalendarItemOccurrenceDto { Id = i.Id, Title = i.Title, PlaceId = i.PlaceId, LocationLabel = i.LocationLabel, IsAllDay = i.IsAllDay, Start = occ, End = duration is { } d ? occ + d : null, CalendarIds = memberIds, Category = i.Category, Status = i.Status, Tags = i.Tags, ParentItemId = i.ParentItemId, ParentTitle = parentTitle, ChildCount = childCount, Completeness = score, Etag = i.ContentHash });
            }
            else if (OccurrenceStart(i) is { } start && start >= windowStart && start < windowEnd)
            {
                results.Add(new CalendarItemOccurrenceDto { Id = i.Id, Title = i.Title, PlaceId = i.PlaceId, LocationLabel = i.LocationLabel, IsAllDay = i.IsAllDay, Start = start, End = duration is { } d ? start + d : i.EndsAt, CalendarIds = memberIds, Category = i.Category, Status = i.Status, Tags = i.Tags, ParentItemId = i.ParentItemId, ParentTitle = parentTitle, ChildCount = childCount, Completeness = score, Etag = i.ContentHash });
            }
        }
        // take/skip count expanded occurrences, not items.
        IEnumerable<CalendarItemOccurrenceDto> page = desc ? results.OrderByDescending(x => x.Start) : results.OrderBy(x => x.Start);
        if (skip is { } sk) page = page.Skip(sk);
        if (take is { } tk) page = page.Take(tk);
        return OpResult<List<CalendarItemOccurrenceDto>>.Ok([.. page]);
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
        if (await ParentInvalidAsync(principalId, r.ParentItemId, ct) is { } parentError)
            return OpResult<CalendarItemDto>.Invalid(parentError);
        var parentItemId = r.ParentItemId ?? item.ParentItemId;

        var fields = new CalendarItemFields(title, description, status, item.IsAllDay, startsAt, endsAt,
            item.StartTimezone, item.EndTimezone, item.StartDate, item.EndDate, rrule,
            item.RecurrenceExceptions, item.RecurrenceOverrides, category, placeId, locationLabel, parentItemId, tags,
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

    /// <summary>Validates a proposed parent link: null id ⇒ ok; returns an error string when the parent is missing,
    /// deleted, or not in a calendar the caller can read. (No cycle check — parenting is a by-convention hint.)</summary>
    private async Task<string?> ParentInvalidAsync(Guid principalId, Guid? parentId, CancellationToken ct)
    {
        if (parentId is not { } pid) return null;
        var parent = await session.LoadAsync<CalendarItem>(pid, ct);
        if (parent is null || parent.DeletedAt is not null) return "Parent item not found.";
        var calIds = await access.AccessibleCalendarIdsAsync(principalId, ct);
        return parent.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && calIds.Contains(m.CalendarId))
            ? null : "No access to the parent item.";
    }

    internal static DateTimeOffset AllDayInstant(DateOnly d) => new(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero);

    internal static DateTimeOffset? OccurrenceStart(CalendarItem i)
    {
        if (i.IsAllDay && i.StartDate is { } d) return AllDayInstant(d);
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
