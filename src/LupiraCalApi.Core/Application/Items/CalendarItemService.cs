using System.Text.Json.Nodes;
using Lupira.Contracts.Fires;
using Lupira.Marten.Idempotency;
using Lupira.Primitives;
using Lupira.Results;
using LupiraCalApi.Core.Abstractions;
using LupiraCalApi.Core.Application.Dav;
using LupiraCalApi.Core.Application.Subscriptions;
using LupiraCalApi.Core.Auth;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.CalendarItems.Events;
using LupiraCalApi.Core.Domain.Calendars;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Mappers;
using LupiraCalApi.Core.Serialization;
using Marten;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Core.Application.Items;

/// <summary>
/// The calendar-item core shared by REST, DAV, and MCP. Every mutation appends events to the item's Marten stream;
/// the inline <see cref="CalendarItem"/> snapshot is the read model. The raw <c>SourceIcalendar</c> blob + content
/// hash ride on the events (DAV source of truth + ETag). Items are calendar-independent (many-to-many via curation).
/// Mutations may carry an <c>Idempotency-Key</c> command id (see <see cref="Idempotency"/>) and an
/// <c>occurredAt</c> client stamp (see <see cref="SectionLww"/>); creates need neither — <c>SourceKey</c>
/// already makes them replay-safe.
/// </summary>
public sealed class CalendarItemService(IDocumentSession session, AccessResolver access, RecurrenceExpander expander, IGeoResolver geo, CompletenessResolver completeness, IContactResolver contacts, Idempotency idempotency, IOptions<ItemTimeZoneOptions> zones, SubscribedOccurrenceReader subscribed)
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

    private const string TravelUnresolved = "A travel location could not be resolved to a place (geo unavailable) — retry.";
    private const string LocationNeedsPlaceId = "Resolve the location to a LupiraGeoApi place first and pass PlaceId, or set LocationUnresolved to keep it as a label only.";
    private const string UnresolvedWithPlaceId = "LocationUnresolved cannot be combined with PlaceId.";
    private const string PlaceUnverified = "A place id could not be verified (geo unavailable) — retry.";
    private const string SourceKeyForeign = "This source key belongs to an item you can't access.";

    /// <summary>REST/MCP take a pre-resolved place (Location, if any, is only its label) or, with LocationUnresolved, a
    /// label-only location. Free-text resolution lives on the CalDAV path (PutIcsAsync).</summary>
    internal static OpResult<(Guid? PlaceId, string? Label)> LocationOnCreate(CreateCalendarItemRequest r)
    {
        if (r.LocationUnresolved)
            return r.PlaceId is null ? OpResult<(Guid?, string?)>.Ok((null, Trimmed(r.Location))) : OpResult<(Guid?, string?)>.Invalid(UnresolvedWithPlaceId);
        if (r.PlaceId is { } pid) return OpResult<(Guid?, string?)>.Ok((pid, Trimmed(r.Location)));
        return Trimmed(r.Location) is null ? OpResult<(Guid?, string?)>.Ok((null, null)) : OpResult<(Guid?, string?)>.Invalid(LocationNeedsPlaceId);
    }

    /// <summary>Update counterpart of <see cref="LocationOnCreate"/>. A label the item already carries passes through
    /// unanchored so offline clients replaying the full core section don't break.</summary>
    internal static OpResult<(Guid? PlaceId, string? Label)> LocationOnUpdate(UpdateCalendarItemRequest r, CalendarItem item)
    {
        if (r.LocationUnresolved)
            return r.PlaceId is null ? OpResult<(Guid?, string?)>.Ok((null, Trimmed(r.Location))) : OpResult<(Guid?, string?)>.Invalid(UnresolvedWithPlaceId);
        var label = Trimmed(r.Location);
        if (r.PlaceIdProvided || r.PlaceId is not null)
        {
            if (r.PlaceId is null && label is not null && label != item.LocationLabel) return OpResult<(Guid?, string?)>.Invalid(LocationNeedsPlaceId);
            // A moved anchor invalidates the old label; matches create (no label ⇒ null).
            return OpResult<(Guid?, string?)>.Ok((r.PlaceId, r.Location is not null ? label : r.PlaceId == item.PlaceId ? item.LocationLabel : null));
        }

        return r.Location is not null && label != item.LocationLabel
            ? OpResult<(Guid?, string?)>.Invalid(LocationNeedsPlaceId)
            : OpResult<(Guid?, string?)>.Ok((item.PlaceId, item.LocationLabel));
    }

    /// <summary>Checks supplied place ids against geo and maps each to its surviving id (a merged-away id redirects).
    /// Ids in <paramref name="stored"/> are already on the item and pass unchecked. Geo unconfigured (dev/test) ⇒ ids pass as-is.</summary>
    private async Task<OpResult<Dictionary<Guid, Guid>>> CanonicalPlacesAsync(IEnumerable<Guid?> supplied, IEnumerable<Guid> stored, CancellationToken ct)
    {
        var ids = supplied.OfType<Guid>().Except(stored).Distinct().ToList();
        if (ids.Count == 0 || !geo.IsConfigured) return OpResult<Dictionary<Guid, Guid>>.Ok([]);
        if (await geo.LookupAsync(ids, ct) is not { } found) return OpResult<Dictionary<Guid, Guid>>.Unavailable(PlaceUnverified);
        var unknown = ids.Where(id => !found.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
            return OpResult<Dictionary<Guid, Guid>>.Invalid(
                $"Unknown place id {string.Join(", ", unknown)}: no such LupiraGeoApi place. Resolve or create the place in geo first.");
        return OpResult<Dictionary<Guid, Guid>>.Ok(found.ToDictionary(kv => kv.Key, kv => kv.Value.PlaceId));
    }

    private static Guid? Canonical(Dictionary<Guid, Guid> survivors, Guid? id) =>
        id is { } g ? survivors.GetValueOrDefault(g, g) : null;

    private static ItemDetails? WithCanonicalTravel(ItemDetails? details, Dictionary<Guid, Guid> survivors) =>
        details?.Travel is { } leg
            ? details with { Travel = leg with { ToPlaceId = Canonical(survivors, leg.ToPlaceId), FromPlaceId = Canonical(survivors, leg.FromPlaceId) } }
            : details;

    /// <summary>The zone a timed item written without one gets: the first of its calendars carrying a valid zone, else
    /// <see cref="ItemTimeZoneOptions.DefaultTimezone"/>.</summary>
    private async Task<string> DefaultZoneAsync(IEnumerable<Guid> calendarIds, CancellationToken ct)
    {
        foreach (var calendarId in calendarIds)
        {
            if (await session.LoadAsync<Calendar>(calendarId, ct) is { DefaultTimezone: { } id } && TimeZoneIds.Find(id) is not null)
                return id;
        }

        return zones.Value.DefaultTimezone;
    }

    public async Task<OpResult<CalendarItemDto>> CreateAsync(Guid principalId, CreateCalendarItemRequest r, CancellationToken ct = default)
    {
        if (r.CalendarId is { } pre && !await access.CanWriteCalendarAsync(principalId, pre, ct))
            return OpResult<CalendarItemDto>.Forbidden("No write access to this calendar.");
        // Birthdays content is synthesized from contacts at read time — stored items filed there would shadow it.
        if (r.CalendarId is { } target && await session.LoadAsync<Calendar>(target, ct) is { Kind: CalendarKind.Birthdays })
            return OpResult<CalendarItemDto>.Invalid("Items cannot be created in the Birthdays calendar — it is synthesized from contact birthdays.");

        // A client SourceKey pins the stream id + external UID (idempotent import replay); else a random uid.
        var hasKey = !string.IsNullOrWhiteSpace(r.SourceKey);
        var uid = hasKey ? r.SourceKey!.Trim() : $"{Guid.NewGuid():N}@cal.lupira.com";
        var id = DeterministicGuid.From(uid);
        var stream = hasKey ? await session.Events.FetchForWriting<CalendarItem>(id, ct) : null;
        if (stream?.Aggregate is { } prior && !await MayReuseSourceKeyAsync(principalId, prior, ct))
            return OpResult<CalendarItemDto>.Forbidden(SourceKeyForeign);
        if (stream?.Aggregate is { DeletedAt: null } live)
            return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(live, ct));   // idempotent hit — no new events

        var location = LocationOnCreate(r);
        if (!location.IsOk) return OpResult<CalendarItemDto>.Invalid(location.Error!);
        var (placeId, locationLabel) = location.Value;

        if (!TryParseDefined<ItemStatus>(r.Status, out var status)) return OpResult<CalendarItemDto>.Invalid(UnknownEnum<ItemStatus>("status", r.Status!));
        if (!TryParseDefined<ItemCategory>(r.Category, out var category)) return OpResult<CalendarItemDto>.Invalid(UnknownEnum<ItemCategory>("category", r.Category!));
        if (ItemDetailsMapper.Validate(category, r.Details) is { } detailsError)
            return OpResult<CalendarItemDto>.Invalid(detailsError);
        var places = await CanonicalPlacesAsync([placeId, r.Details?.Travel?.ToPlaceId, r.Details?.Travel?.FromPlaceId], [], ct);
        if (!places.IsOk) return new OpResult<CalendarItemDto>(places.Status, null, places.Error) { Type = places.Type };
        placeId = Canonical(places.Value!, placeId);
        var (details, detailsUnresolved) = await ItemDetailsMapper.BuildAsync(r.Details, r.Availability, geo, ct);
        if (detailsUnresolved) return OpResult<CalendarItemDto>.Unavailable(TravelUnresolved);
        details = WithCanonicalTravel(details, places.Value!);

        var startTimezone = string.IsNullOrWhiteSpace(r.StartTimezone) ? null : r.StartTimezone;
        string? endTimezone = null;
        if (!r.IsAllDay && r.StartsAt is not null)
        {
            startTimezone ??= await DefaultZoneAsync(r.CalendarId is { } zoneCal ? [zoneCal] : [], ct);
            endTimezone = startTimezone;
        }

        // Parent by explicit id, else by the parent's SourceKey (batch imports resolve it deterministically).
        var parentItemId = r.ParentItemId ?? (string.IsNullOrWhiteSpace(r.ParentSourceKey) ? (Guid?) null : DeterministicGuid.From(r.ParentSourceKey!.Trim()));
        if (await ParentInvalidAsync(principalId, parentItemId, ct) is { } parentError)
            return OpResult<CalendarItemDto>.Invalid(parentError);

        var fields = new CalendarItemFields(r.Title, r.Description, status, r.IsAllDay, r.StartsAt, r.EndsAt,
            startTimezone, endTimezone, r.StartDate, r.EndDate, r.RecurrenceRule, null, null, null, category, placeId, locationLabel, parentItemId, r.Tags,
            r.StartPrecision, r.EndPrecision, r.Color);

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
    /// the whole batch on one bad item — returns a per-item <see cref="ItemBatchStatus"/>. Results are in input order.</summary>
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
                && await session.LoadAsync<CalendarItem>(DeterministicGuid.From(r.SourceKey!.Trim()), ct) is { DeletedAt: null } existing
                && await MayReuseSourceKeyAsync(principalId, existing, ct))
            {
                byRequest[r] = new ItemBatchResult { SourceKey = r.SourceKey, ItemId = existing.Id, Status = ItemBatchStatus.Existed, Error = null };
                continue;
            }

            var res = await CreateAsync(principalId, r, ct);
            byRequest[r] = res.Status switch
            {
                OpStatus.Ok => new ItemBatchResult { SourceKey = r.SourceKey, ItemId = res.Value!.Id, Status = ItemBatchStatus.Created, Error = null },
                OpStatus.Forbidden => new ItemBatchResult { SourceKey = r.SourceKey, ItemId = null, Status = ItemBatchStatus.Forbidden, Error = res.Error },
                OpStatus.Unavailable => new ItemBatchResult { SourceKey = r.SourceKey, ItemId = null, Status = ItemBatchStatus.Unavailable, Error = res.Error },
                _ => new ItemBatchResult { SourceKey = r.SourceKey, ItemId = null, Status = ItemBatchStatus.Invalid, Error = res.Error },
            };
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

            if (!progressed)
            {
                order.AddRange(pending);
                break;
            }
        }

        return order;
    }

    public async Task<OpResult<List<CalendarItemOccurrenceDto>>> SearchAsync(
        Guid principalId, string? query, DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId, string? tag, Guid? parentId, Guid? contactId,
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

        var items = candidates.Where(i =>
            i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && searchIds.Contains(m.CalendarId)));
        if (!string.IsNullOrWhiteSpace(tag)) items = items.Where(i => i.Tags is not null && i.Tags.Contains(tag));
        if (parentId is { } parent) items = items.Where(i => i.ParentItemId == parent);
        if (contactId is { } contact) items = items.Where(i => i.Attendees.Any(a => a.ContactId == contact));
        if (cat is not null) items = items.Where(i => i.Category == cat);
        if (st is not null) items = items.Where(i => i.Status == st);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            items = items.Where(i =>
                (i.Title ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (i.Description ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var itemList = items.ToList();
        var scores = await completeness.ScoreItemsAsync(itemList, ct);

        // Text queries and parent/contact drill-downs default to all-time; browsing keeps the ±1y window.
        var allTime = !string.IsNullOrWhiteSpace(query) || parentId is not null || contactId is not null;
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
            foreach (var slot in OccurrenceSlots.Of(i, expander, windowStart, windowEnd, expansionEnd))
            {
                var change = slot.Change;
                results.Add(new CalendarItemOccurrenceDto { Id = i.Id, Title = change?.Title ?? i.Title, PlaceId = i.PlaceId, LocationLabel = change?.LocationLabel ?? i.LocationLabel, IsAllDay = i.IsAllDay, Start = slot.Start, End = slot.End, OriginalStart = slot.OriginalStart, IsChanged = change is not null, CalendarIds = memberIds, Category = i.Category, Color = i.Color, Status = change?.Status ?? i.Status, Tags = i.Tags, ParentItemId = i.ParentItemId, ParentTitle = parentTitle, ChildCount = childCount, Completeness = score, Etag = i.ContentHash });
            }
        }

        // Birthdays are not stored items — they're synthesized at read time from LupiraContactApi whenever a
        // Birthdays-kind calendar is in scope and no item-relational filter (tag/parent/contact/category) excludes them.
        if (tag is null && parentId is null && contactId is null && cat is null && (st is null || st == ItemStatus.Confirmed))
        {
            var bdayCalIds = (await session.Query<Calendar>().Where(c => c.Kind == CalendarKind.Birthdays).ToListAsync(ct))
                .Select(c => c.Id).Where(searchIds.Contains).ToArray();
            if (bdayCalIds.Length > 0)
                results.AddRange(await SynthesizeBirthdaysAsync(bdayCalIds, query, from, to, ct));
        }

        // Subscribed feeds carry no tags, parents, attendees or categories, so those filters exclude them.
        if (tag is null && parentId is null && contactId is null && cat is null)
            results.AddRange(await subscribed.OccurrencesAsync([.. searchIds], query, windowStart, windowEnd, expansionEnd, st, ct));

        // take/skip count expanded occurrences, not items.
        IEnumerable<CalendarItemOccurrenceDto> page = desc ? results.OrderByDescending(x => x.Start) : results.OrderBy(x => x.Start);
        if (skip is { } sk) page = page.Skip(sk);
        if (take is { } tk) page = page.Take(tk);
        return OpResult<List<CalendarItemOccurrenceDto>>.Ok([.. page]);
    }

    /// <summary>Reverse index: items anchored to a place — as their location (<c>PlaceId</c>) or a travel/car endpoint.
    /// Full items (not recurrence-expanded), scoped to calendars the principal can read. Merge-aware: items anchored to a
    /// place geo has since merged away are listed under its survivor (and the merged-away id finds them too).</summary>
    public async Task<OpResult<List<CalendarItemDto>>> ByPlaceAsync(Guid principalId, Guid placeId, CancellationToken ct = default)
    {
        var calIds = await access.AccessibleCalendarIdsAsync(principalId, ct);
        var candidates = await session.Query<CalendarItem>().Where(i => i.DeletedAt == null).ToListAsync(ct);
        var readable = candidates
            .Where(i => i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && calIds.Contains(m.CalendarId)))
            .ToList();
        var survivors = await SurvivorIdsAsync(readable, placeId, ct);
        var target = survivors.GetValueOrDefault(placeId, placeId);
        var items = readable
            .Where(i => PlaceRefs(i).Any(p => survivors.GetValueOrDefault(p, p) == target))
            .OrderBy(i => i.StartsAt).ThenBy(i => i.Title)
            .ToList();
        var scores = await completeness.ScoreItemsAsync(items, ct);
        return OpResult<List<CalendarItemDto>>.Ok(items.Select(i => i.ToResponse(scores[i.Id])).ToList());
    }

    // Geo merges never rewrite the ids stored on items — a merged-away id only redirects to its survivor on read — so
    // every referenced id is mapped through geo before matching. Geo unavailable ⇒ empty map ⇒ exact-id matching.
    private async Task<Dictionary<Guid, Guid>> SurvivorIdsAsync(IEnumerable<CalendarItem> items, Guid placeId, CancellationToken ct)
    {
        if (!geo.IsConfigured) return [];
        var ids = items.SelectMany(PlaceRefs).Append(placeId).Distinct().ToList();
        var found = await geo.LookupAsync(ids, ct);
        return found?.ToDictionary(kv => kv.Key, kv => kv.Value.PlaceId) ?? [];
    }

    private static IEnumerable<Guid> PlaceRefs(CalendarItem i) =>
        new[] { i.PlaceId, i.Details?.Travel?.ToPlaceId, i.Details?.Travel?.FromPlaceId }.OfType<Guid>();

    /// <summary>Check-in worklist: scoreable items ranked thinnest-first (score asc, most recent start first on ties).
    /// Item-granular — no recurrence expansion. Exempt items (system/Birthdays/Availability calendars, cancelled,
    /// presence/payload) carry no score and are excluded.</summary>
    public async Task<OpResult<List<CalendarItemDto>>> ThinItemsAsync(
        Guid principalId, Guid? calendarId = null, string? category = null, double? maxScore = null, int? take = null, CancellationToken ct = default)
    {
        if (!TryParseDefined<ItemCategory>(category, out var cat))
            return OpResult<List<CalendarItemDto>>.Invalid(UnknownEnum<ItemCategory>("category", category!));
        if (maxScore is < 0 or > 1) return OpResult<List<CalendarItemDto>>.Invalid("maxScore must be between 0 and 1.");
        if (take is < 1) return OpResult<List<CalendarItemDto>>.Invalid("take must be >= 1.");

        var accessibleIds = await access.AccessibleCalendarIdsAsync(principalId, ct);
        var searchIds = accessibleIds;
        if (calendarId is { } cid)
        {
            if (!accessibleIds.Contains(cid)) return OpResult<List<CalendarItemDto>>.Forbidden("No access to this calendar.");
            searchIds = [cid];
        }

        var candidates = await session.Query<CalendarItem>().Where(i => i.DeletedAt == null).ToListAsync(ct);
        var items = candidates
            .Where(i => i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && searchIds.Contains(m.CalendarId))
                && (cat is null || i.Category == cat))
            .ToList();

        var scores = await completeness.ScoreItemsAsync(items, ct);
        var thin = items
            .Select(i => (Item: i, Score: scores[i.Id]))
            .Where(x => x.Score is not null && x.Score.Score < (maxScore ?? 1.0))
            .OrderBy(x => x.Score!.Score).ThenByDescending(x => x.Item.StartsAt)
            .Take(take ?? 25);
        return OpResult<List<CalendarItemDto>>.Ok([.. thin.Select(x => x.Item.ToResponse(x.Score))]);
    }

    public async Task<OpResult<CalendarItemDto>> GetAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var item = await session.LoadAsync<CalendarItem>(id, ct);
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        if (!await access.CanReadItemAsync(principalId, item, ct))
            return OpResult<CalendarItemDto>.Forbidden("No access to this item.");
        return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(item, ct));
    }

    public async Task<OpResult<CalendarItemDto>> UpdateAsync(Guid principalId, Guid id, UpdateCalendarItemRequest r, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await ReplayedAsync(id, ct);
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult<CalendarItemDto>.Forbidden("No write access to this item.");
        var revision = await ReviseAsync(principalId, item, r, ct);
        if (!revision.IsOk) return new OpResult<CalendarItemDto>(revision.Status, null, revision.Error) { Type = revision.Type };

        stream.AppendOne(new ItemRevised(id, revision.Value!.Fields, revision.Value.Details, r.OccurredAt, commandId));
        await idempotency.CommitAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        var updated = await session.LoadAsync<CalendarItem>(id, ct);
        return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(updated!, ct));
    }

    /// <summary>What <paramref name="current"/> becomes under <paramref name="r"/>: its fields, and its details (null =
    /// unchanged). Shared by the whole-item update and the series that continues a split.</summary>
    private async Task<OpResult<Revision>> ReviseAsync(Guid principalId, CalendarItem current, UpdateCalendarItemRequest r, CancellationToken ct)
    {
        if (!TryParseDefined<ItemStatus>(r.Status, out var statusIn)) return OpResult<Revision>.Invalid(UnknownEnum<ItemStatus>("status", r.Status!));
        if (!TryParseDefined<ItemCategory>(r.Category, out var categoryIn)) return OpResult<Revision>.Invalid(UnknownEnum<ItemCategory>("category", r.Category!));

        var title = r.Title ?? current.Title;
        var description = r.Description ?? current.Description;
        var status = statusIn ?? current.Status;
        // Sentinel merge: Provided writes the value verbatim (incl. null = clear); else omitted ⇒ kept.
        var startsAt = r.StartsAtProvided ? r.StartsAt : r.StartsAt ?? current.StartsAt;
        var endsAt = r.EndsAtProvided ? r.EndsAt : r.EndsAt ?? current.EndsAt;
        var isAllDay = r.IsAllDay ?? current.IsAllDay;
        var startDate = r.StartDateProvided ? r.StartDate : r.StartDate ?? current.StartDate;
        var endDate = r.EndDateProvided ? r.EndDate : r.EndDate ?? current.EndDate;
        var startTimezone = r.StartTimezoneProvided ? r.StartTimezone : r.StartTimezone ?? current.StartTimezone;
        var endTimezone = r.EndTimezoneProvided ? r.EndTimezone : r.EndTimezone ?? current.EndTimezone;
        var rrule = r.RecurrenceRuleProvided ? r.RecurrenceRule : r.RecurrenceRule ?? current.RecurrenceRule;
        var tags = r.Tags ?? current.Tags;
        var color = r.ColorProvided ? r.Color : r.Color ?? current.Color;
        if (isAllDay && startDate is null) return OpResult<Revision>.Invalid("An all-day item needs StartDate.");
        var location = LocationOnUpdate(r, current);
        if (!location.IsOk) return OpResult<Revision>.Invalid(location.Error!);
        var (placeId, locationLabel) = location.Value;

        var category = categoryIn ?? current.Category;
        var categoryChanged = category != current.Category;

        // Validate against the resolved category (an omitted r.Category means "the item's current category").
        if (ItemDetailsMapper.Validate(category, r.Details) is { } detailsError)
            return OpResult<Revision>.Invalid(detailsError);
        var places = await CanonicalPlacesAsync([placeId, r.Details?.Travel?.ToPlaceId, r.Details?.Travel?.FromPlaceId], PlaceRefs(current), ct);
        if (!places.IsOk) return new OpResult<Revision>(places.Status, null, places.Error) { Type = places.Type };
        placeId = Canonical(places.Value!, placeId);
        if (r.ParentItemId == current.Id) return OpResult<Revision>.Invalid("An item cannot be its own parent.");
        if (await ParentInvalidAsync(principalId, r.ParentItemId, ct) is { } parentError)
            return OpResult<Revision>.Invalid(parentError);
        var parentItemId = r.ParentItemIdProvided ? r.ParentItemId : r.ParentItemId ?? current.ParentItemId;

        var becameTimed = current.IsAllDay || current.StartsAt is null;
        if (!isAllDay && startsAt is not null && string.IsNullOrWhiteSpace(startTimezone) && (becameTimed || !string.IsNullOrWhiteSpace(r.RecurrenceRule)))
        {
            startTimezone = await DefaultZoneAsync(current.Calendars.Where(m => m.Status == CalendarEntryStatus.Accepted).Select(m => m.CalendarId), ct);
            if (string.IsNullOrWhiteSpace(endTimezone)) endTimezone = startTimezone;
        }

        // Moving the series (time, day, zone, rule) carries its end and its per-occurrence changes along with their occurrences.
        var before = RecurringSeries.Of(current);
        var after = new RecurringSeries(isAllDay, startsAt, startDate, startTimezone, rrule).KeepingBoundOf(before);
        var deviations = SeriesDeviations.Of(current).Follow(before, after, current.ExtraOccurrences, expander);

        var fields = new CalendarItemFields(title, description, status, isAllDay, startsAt, endsAt,
            startTimezone, endTimezone, startDate, endDate, after.RecurrenceRule,
            deviations.Excluded, current.ExtraOccurrences, deviations.Overrides, category, placeId, locationLabel, parentItemId, tags,
            r.StartPrecision ?? current.StartPrecision, r.EndPrecision ?? current.EndPrecision, color);

        var (incoming, detailsUnresolved) = await ItemDetailsMapper.BuildAsync(r.Details, r.Availability, geo, ct);
        if (detailsUnresolved) return OpResult<Revision>.Unavailable(TravelUnresolved);
        incoming = WithCanonicalTravel(incoming, places.Value!);
        // null incoming keeps existing details (Apply only overwrites when non-null); reclassifying with none clears the previous details.
        var details = incoming is null
            ? (categoryChanged ? new ItemDetails() : null)
            : (categoryChanged ? incoming : ItemDetailsMapper.Merge(current.Details, incoming));

        return OpResult<Revision>.Ok(new Revision(fields, details));
    }

    public async Task<OpResult> DeleteAsync(Guid principalId, Guid id, Guid? commandId = null, CancellationToken ct = default)
    {
        // A replayed delete after a lost response finds the item already gone — 404 would wedge an offline
        // client's outbox, so the ledger turns it into an idempotent success.
        if (await idempotency.SeenAsync(commandId, ct) is not null) return OpResult.Ok();
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult.Forbidden("No write access to this item.");
        stream.AppendOne(new ItemDeleted(id, DateTimeOffset.UtcNow));
        await idempotency.CommitAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        return OpResult.Ok();
    }

    public async Task<OpResult<CalendarItemDto>> AttachMetadataAsync(Guid principalId, Guid id, JsonObject patch, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await ReplayedAsync(id, ct);
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult<CalendarItemDto>.Forbidden("No write access to this item.");

        var current = JsonNode.Parse(string.IsNullOrWhiteSpace(item.Metadata) ? "{}" : item.Metadata)!.AsObject();
        foreach (var kv in patch) current[kv.Key] = kv.Value?.DeepClone();
        stream.AppendOne(new ItemMetadataAttached(id, current.ToJsonString(), occurredAt, commandId));
        await idempotency.CommitAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        var updated = await session.LoadAsync<CalendarItem>(id, ct);
        return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(updated!, ct));
    }

    /// <summary>Response for a mutation whose command id is already in the ledger: the current state, deleted or
    /// not — the original call succeeded, so the replay must too.</summary>
    private async Task<OpResult<CalendarItemDto>> ReplayedAsync(Guid id, CancellationToken ct) =>
        await session.LoadAsync<CalendarItem>(id, ct) is { } current
            ? OpResult<CalendarItemDto>.Ok(await ToDtoAsync(current, ct))
            : OpResult<CalendarItemDto>.NotFound();

    // ---- event-bound payload (server-side only, XOR — an item carries one prompt OR one action) ----

    public async Task<OpResult<CalendarItemDto>> SetPromptAsync(Guid principalId, Guid id, ItemPrompt prompt, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await ReplayedAsync(id, ct);
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult<CalendarItemDto>.Forbidden("No write access to this item.");
        if (item.Action is not null) return OpResult<CalendarItemDto>.Conflict("Item already carries an action; clear it first.");

        stream.AppendOne(new ItemPromptSet(id, prompt, occurredAt, commandId));
        await idempotency.CommitAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        var updated = await session.LoadAsync<CalendarItem>(id, ct);
        return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(updated!, ct));
    }

    public async Task<OpResult<CalendarItemDto>> SetActionAsync(Guid principalId, Guid id, ItemAction action, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await ReplayedAsync(id, ct);
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult<CalendarItemDto>.Forbidden("No write access to this item.");
        if (item.Prompt is not null) return OpResult<CalendarItemDto>.Conflict("Item already carries a prompt; clear it first.");

        stream.AppendOne(new ItemActionSet(id, action, occurredAt, commandId));
        await idempotency.CommitAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        var updated = await session.LoadAsync<CalendarItem>(id, ct);
        return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(updated!, ct));
    }

    /// <summary>Exclude or override one occurrence of a recurring item, keyed by its unmodified start.</summary>
    public async Task<OpResult<CalendarItemDto>> ChangeOccurrenceAsync(Guid principalId, Guid id, DateTimeOffset originalStart, ChangeOccurrenceRequest r, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await ReplayedAsync(id, ct);
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult<CalendarItemDto>.Forbidden("No write access to this item.");
        var original = originalStart.ToUniversalTime();
        if (OccurrenceUnknown(item, original) is { } unknown) return OpResult<CalendarItemDto>.Invalid(unknown, ProblemTypes.OccurrenceNotInSeries);
        if (!TryParseDefined<ItemStatus>(r.Status, out var status)) return OpResult<CalendarItemDto>.Invalid(UnknownEnum<ItemStatus>("status", r.Status!));
        if (!r.Excluded && r is { StartsAt: null, EndsAt: null, Title: null, Description: null } && status is null)
            return OpResult<CalendarItemDto>.Invalid("Nothing to change; restore the occurrence to drop its change.");
        if (r.StartsAt is { } s && r.EndsAt is { } e && e < s) return OpResult<CalendarItemDto>.Invalid("EndsAt precedes StartsAt.");
        if (item.IsAllDay && new[] { r.StartsAt, r.EndsAt }.Any(t => t is { } x && x.ToUniversalTime().TimeOfDay != TimeSpan.Zero))
            return OpResult<CalendarItemDto>.Invalid("An all-day occurrence starts and ends on dates: send 00:00Z (EndsAt is the last day).");

        var keptLocationLabel = item.OccurrenceOverrides?.FirstOrDefault(o => o.OriginalStart == original)?.LocationLabel;
        stream.AppendOne(r.Excluded
            ? new OccurrenceExcluded(id, original, r.OccurredAt, commandId)
            : new OccurrenceOverridden(id, new OccurrenceOverride(original, r.StartsAt?.ToUniversalTime(), r.EndsAt?.ToUniversalTime(),
                r.Title, r.Description, status, keptLocationLabel), r.OccurredAt, commandId));
        await idempotency.CommitAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        var updated = await session.LoadAsync<CalendarItem>(id, ct);
        return OpResult<CalendarItemDto>.Ok(await ToDtoAsync(updated!, ct));
    }

    /// <summary>Revert one occurrence of a recurring item to the series.</summary>
    public async Task<OpResult> RestoreOccurrenceAsync(Guid principalId, Guid id, DateTimeOffset originalStart, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return OpResult.Ok();
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult.Forbidden("No write access to this item.");
        var original = originalStart.ToUniversalTime();
        if (OccurrenceUnknown(item, original) is not null) return OpResult.Ok();
        var changed = (item.ExcludedOccurrences ?? []).Contains(original) || (item.OccurrenceOverrides ?? []).Any(o => o.OriginalStart == original);
        if (!changed) return OpResult.Ok();   // already the series default; don't append a meaningless event

        stream.AppendOne(new OccurrenceRestored(id, original, occurredAt, commandId));
        await idempotency.CommitAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        return OpResult.Ok();
    }

    /// <summary>Change one occurrence of a recurring item and every one after it: the series ends before it, and a series
    /// continuing it takes the rest with the changes — unless they're deleted. At the first occurrence the whole series
    /// changes or goes.</summary>
    public async Task<OpResult<SeriesSplitDto>> ChangeFollowingOccurrencesAsync(
        Guid principalId, Guid id, DateTimeOffset originalStart, ChangeFollowingOccurrencesRequest r, Guid? commandId = null, CancellationToken ct = default)
    {
        var uid = string.IsNullOrWhiteSpace(r.SourceKey) ? $"{commandId ?? Guid.NewGuid():N}@cal.lupira.com" : r.SourceKey.Trim();
        var continuationId = DeterministicGuid.From(uid);
        if (await idempotency.SeenAsync(commandId, ct) is not null) return OpResult<SeriesSplitDto>.Ok(await SplitStateAsync(id, continuationId, r.Excluded, ct));
        if (!r.Excluded && r.Changes is null) return OpResult<SeriesSplitDto>.Invalid("Nothing to change; send Changes, or Excluded=true to delete these occurrences.");
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<SeriesSplitDto>.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult<SeriesSplitDto>.Forbidden("No write access to this item.");
        if (!r.Excluded && await session.LoadAsync<CalendarItem>(continuationId, ct) is { } taken)
            return taken is { DeletedAt: null } && taken.ContinuesItemId == id
                ? OpResult<SeriesSplitDto>.Ok(await SplitStateAsync(id, continuationId, excluded: false, ct))
                : OpResult<SeriesSplitDto>.Conflict("This source key already names another item.");
        var at = originalStart.ToUniversalTime();
        var series = RecurringSeries.Of(item);
        if (!series.Recurs) return OpResult<SeriesSplitDto>.Invalid("Only a recurring item has following occurrences.");
        if (!expander.Generates(series, at))
            return OpResult<SeriesSplitDto>.Invalid("No occurrence of this series' rule starts at that time (use the unmodified start, UTC; an added one-off occurrence can't start a series).");
        var occurredAt = (r.OccurredAt ?? r.Changes?.OccurredAt)?.ToUniversalTime();
        if (occurredAt is { } stamp && !SectionLww.Wins(stamp, commandId ?? Guid.Empty, item.CoreTs, item.CoreCmd))
            return OpResult<SeriesSplitDto>.Conflict("The series changed after this edit was made.");

        var version = (int) (stream.CurrentVersion ?? 0) + 1;
        if (SeriesSplit.At(item, at, expander) is not { } split)
        {
            if (r.Excluded)
            {
                stream.AppendOne(new ItemDeleted(id, DateTimeOffset.UtcNow));
                await idempotency.CommitAsync(commandId, id, version, ct);
                return OpResult<SeriesSplitDto>.Ok(new SeriesSplitDto());
            }

            var whole = await ReviseAsync(principalId, item, r.Changes!, ct);
            if (!whole.IsOk) return new OpResult<SeriesSplitDto>(whole.Status, null, whole.Error) { Type = whole.Type };
            stream.AppendOne(new ItemRevised(id, whole.Value!.Fields, whole.Value.Details, occurredAt, commandId));
            await idempotency.CommitAsync(commandId, id, version, ct);
            return OpResult<SeriesSplitDto>.Ok(new SeriesSplitDto { Following = await LiveDtoAsync(id, ct) });
        }

        var earlier = new ItemRevised(id, FieldsOf(item) with
        {
            RecurrenceRule = split.Earlier.Series.RecurrenceRule, ExcludedOccurrences = split.Earlier.Excluded,
            ExtraOccurrences = split.Earlier.Extra, OccurrenceOverrides = split.Earlier.Overrides,
        }, null, occurredAt, commandId);
        if (r.Excluded)
        {
            stream.AppendOne(earlier);
            await idempotency.CommitAsync(commandId, id, version, ct);
            return OpResult<SeriesSplitDto>.Ok(new SeriesSplitDto { Earlier = await LiveDtoAsync(id, ct) });
        }

        var following = await ReviseAsync(principalId, Continuation(item, continuationId, split.Following), r.Changes!, ct);
        if (!following.IsOk) return new OpResult<SeriesSplitDto>(following.Status, null, following.Error) { Type = following.Type };

        stream.AppendOne(earlier);
        session.Events.StartStream<CalendarItem>(continuationId, [.. ContinuationEvents(item, continuationId, uid, following.Value!, occurredAt, commandId)]);
        foreach (var relation in await session.Query<Relation>().Where(x => x.FromKind == "item" && x.FromId == id).ToListAsync(ct))
            session.Store(new Relation
            {
                Id = DeterministicGuid.From($"relation:{continuationId}:{relation.Id}"), FromKind = relation.FromKind, FromId = continuationId,
                ToKind = relation.ToKind, ToRef = relation.ToRef, RelationType = relation.RelationType, Metadata = relation.Metadata,
            });
        await idempotency.CommitAsync(commandId, id, version, ct);
        return OpResult<SeriesSplitDto>.Ok(new SeriesSplitDto { Earlier = await LiveDtoAsync(id, ct), Following = await LiveDtoAsync(continuationId, ct) });
    }

    private async Task<SeriesSplitDto> SplitStateAsync(Guid id, Guid continuationId, bool excluded, CancellationToken ct)
    {
        var original = await LiveDtoAsync(id, ct);
        if (excluded) return new SeriesSplitDto { Earlier = original };
        return await LiveDtoAsync(continuationId, ct) is { } continuation
            ? new SeriesSplitDto { Earlier = original, Following = continuation }
            : new SeriesSplitDto { Following = original };
    }

    private async Task<CalendarItemDto?> LiveDtoAsync(Guid id, CancellationToken ct) =>
        await session.LoadAsync<CalendarItem>(id, ct) is { DeletedAt: null } item ? await ToDtoAsync(item, ct) : null;

    private static CalendarItem Continuation(CalendarItem item, Guid id, SeriesPart part) => new()
    {
        Id = id, Title = item.Title, Description = item.Description, Status = item.Status, IsAllDay = item.IsAllDay,
        StartsAt = part.Series.StartsAt, EndsAt = part.EndsAt, StartTimezone = item.StartTimezone, EndTimezone = item.EndTimezone,
        StartDate = part.Series.StartDate, EndDate = part.EndDate, StartPrecision = item.StartPrecision, EndPrecision = item.EndPrecision,
        RecurrenceRule = part.Series.RecurrenceRule, ExcludedOccurrences = part.Excluded, ExtraOccurrences = part.Extra, OccurrenceOverrides = part.Overrides,
        Category = item.Category, Color = item.Color, PlaceId = item.PlaceId, LocationLabel = item.LocationLabel, ParentItemId = item.ParentItemId, Tags = item.Tags,
        Details = item.Details, Calendars = item.Calendars,
    };

    // Attended stays with the occurrences it happened at and a withdrawn attendee doesn't continue; the rest carry their invitations.
    private static IEnumerable<object> ContinuationEvents(CalendarItem item, Guid id, string uid, Revision revision, DateTimeOffset? occurredAt, Guid? commandId)
    {
        var at = occurredAt ?? DateTimeOffset.UtcNow;
        Ref? Retarget(Ref? target) => target is { Kind: RefKind.Event } && target.Id == item.Id ? target with { Id = id } : target;

        yield return new ItemScheduled(id, uid, revision.Fields, revision.Details ?? item.Details, item.Id, occurredAt, commandId);
        foreach (var m in item.Calendars.Where(m => m.Status != CalendarEntryStatus.Removed))
            yield return new AddedToCalendar(id, m.CalendarId, m.Status, at, commandId);
        foreach (var a in item.Attendees.Where(a => a.LeftAt is null))
        {
            var participationId = SeriesSplit.ContinuedParticipationId(id, a.ParticipationId);
            yield return new AttendeeInvited(id, participationId, a.ContactId, a.Role, a.InvitedAt ?? at);
            if (a.Status != ParticipationStatus.NeedsAction) yield return new InvitationResponded(id, participationId, a.Status, a.RespondedAt ?? at);
            if (a.Reminder is { } reminder) yield return new ParticipantReminderSet(id, participationId, reminder, a.ReminderTs, a.ReminderCmd);
        }

        if (!string.IsNullOrWhiteSpace(item.Metadata) && item.Metadata != "{}") yield return new ItemMetadataAttached(id, item.Metadata, occurredAt, commandId);
        if (item.Prompt is { } prompt) yield return new ItemPromptSet(id, prompt with { Target = Retarget(prompt.Target) }, occurredAt, commandId);
        if (item.Action is { } action) yield return new ItemActionSet(id, action with { Target = Retarget(action.Target) }, occurredAt, commandId);
    }

    private static CalendarItemFields FieldsOf(CalendarItem i) =>
        new(i.Title, i.Description, i.Status, i.IsAllDay, i.StartsAt, i.EndsAt, i.StartTimezone, i.EndTimezone, i.StartDate, i.EndDate,
            i.RecurrenceRule, i.ExcludedOccurrences, i.ExtraOccurrences, i.OccurrenceOverrides, i.Category, i.PlaceId, i.LocationLabel,
            i.ParentItemId, i.Tags, i.StartPrecision, i.EndPrecision, i.Color);

    private string? OccurrenceUnknown(CalendarItem item, DateTimeOffset originalStart) =>
        string.IsNullOrWhiteSpace(item.RecurrenceRule) ? "Only a recurring item has occurrences to change."
        : expander.HasSeriesOccurrence(item, originalStart) ? null
        : "No occurrence of this series starts at that time (use the unmodified start, UTC).";

    public async Task<OpResult> ClearPromptAsync(Guid principalId, Guid id, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return OpResult.Ok();
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult.Forbidden("No write access to this item.");
        if (item.Prompt is null) return OpResult.Ok();   // no-op; don't append a meaningless event

        stream.AppendOne(new ItemPromptCleared(id, occurredAt, commandId));
        await idempotency.CommitAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        return OpResult.Ok();
    }

    public async Task<OpResult> ClearActionAsync(Guid principalId, Guid id, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return OpResult.Ok();
        var stream = await session.Events.FetchForWriting<CalendarItem>(id, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult.NotFound();
        if (!await CanWriteItemAsync(principalId, item, ct)) return OpResult.Forbidden("No write access to this item.");
        if (item.Action is null) return OpResult.Ok();

        stream.AppendOne(new ItemActionCleared(id, occurredAt, commandId));
        await idempotency.CommitAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
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
        try
        {
            p = ICalSerializer.ParseICalendar(rawIcs);
        }
        catch (FormatException ex)
        {
            return OpResult<DavWriteResult>.Invalid(ex.Message);
        }

        var (placeId, locationLabel, _) = await ResolvePlaceAsync(p.Location, ct);   // DAV stays lenient: external clients send free-text, unresolved → label-only
        var fields = new CalendarItemFields(p.Title, p.Description, null, p.IsAllDay, p.StartsAt, p.EndsAt,
            p.StartTimezone, p.EndTimezone, p.StartDate, p.EndDate, p.RecurrenceRule,
            p.ExcludedOccurrences, p.ExtraOccurrences, p.OccurrenceOverrides, null, placeId, locationLabel, null, null,
            Color: p.Color ?? existing?.Color);

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

    // Source keys are guessable (invitation UIDs, import keys), so the item they pin is reused only by its creator or
    // someone who could already read it (live) or write one of its calendars (soft-deleted).
    private async Task<bool> MayReuseSourceKeyAsync(Guid principalId, CalendarItem existing, CancellationToken ct) =>
        (existing.DeletedAt is null
            ? await access.CanReadItemAsync(principalId, existing, ct)
            : await CanWriteItemAsync(principalId, existing, ct))
        || await CreatorOfAsync(existing.Id, ct) == principalId;

    // The acting principal id is stamped as the event's user name (EventActor); items predating provenance have none.
    private async Task<Guid?> CreatorOfAsync(Guid itemId, CancellationToken ct) =>
        await session.Events.QueryAllRawEvents().Where(e => e.StreamId == itemId && e.Version == 1).FirstOrDefaultAsync(ct) is { UserName: { } actor }
        && Guid.TryParse(actor, out var principalId)
            ? principalId
            : null;

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

    /// <summary>The read-time Birthdays projection: one all-day, yearly-recurring occurrence per contact-with-a-birthday,
    /// expanded across the window. Year-less birthdays still recur on their month-day; a Feb-29 birthday only lands in
    /// leap years (skipped otherwise), matching the RRULE expander. Fails open (empty) when contacts are unavailable.</summary>
    private async Task<IReadOnlyList<CalendarItemOccurrenceDto>> SynthesizeBirthdaysAsync(
        Guid[] calendarIds, string? query, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        if (!contacts.IsConfigured) return [];
        if (await contacts.BirthdaysAsync(ct) is not { Count: > 0 } birthdays) return [];

        // A yearly-recurring synthetic event has no natural all-time bound; default to the ±1y browse window
        // (same ceiling logic as the RRULE expander) unless the caller pins from/to.
        var now = DateTimeOffset.UtcNow;
        var windowStart = from ?? now.AddYears(-1);
        var windowEnd = to ?? now.AddYears(1);
        if (windowEnd <= windowStart) return [];

        var term = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var results = new List<CalendarItemOccurrenceDto>();
        foreach (var b in birthdays)
        {
            if (b.Month is < 1 or > 12 || b.Day < 1) continue;
            var title = $"{b.DisplayName}'s birthday";
            if (term is not null && !title.Contains(term, StringComparison.OrdinalIgnoreCase)) continue;

            var id = DeterministicGuid.From($"birthday:{b.ContactId:N}");
            var etag = $"bday-{b.ContactId:N}-{b.Year?.ToString() ?? "----"}{b.Month:D2}{b.Day:D2}";
            for (var year = windowStart.Year; year <= windowEnd.Year; year++)
            {
                if (b.Day > DateTime.DaysInMonth(year, b.Month)) continue;   // Feb 29 in a non-leap year
                var start = AllDayInstant(new DateOnly(year, b.Month, b.Day));
                if (start < windowStart || start >= windowEnd) continue;
                results.Add(new CalendarItemOccurrenceDto
                {
                    Id = id,
                    Title = title,
                    IsAllDay = true,
                    Start = start,
                    End = null,
                    IsChanged = false,
                    CalendarIds = calendarIds,
                    Status = ItemStatus.Confirmed,
                    ChildCount = 0,
                    Origin = new OccurrenceOrigin { Kind = OriginKind.Birthday, SourceId = b.ContactId },
                    Etag = etag,
                });
            }
        }

        return results;
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
    private static bool TryParseDefined<TEnum>(string? value, out TEnum? parsed)
        where TEnum : struct, Enum
    {
        parsed = null;
        if (value is null) return true;
        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var v) || !Enum.IsDefined(v)) return false;
        parsed = v;
        return true;
    }

    private static string UnknownEnum<TEnum>(string field, string value)
        where TEnum : struct, Enum
        =>
        $"Unknown {field} '{value}'. Valid values: {string.Join(", ", Enum.GetNames<TEnum>())}.";

    private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed record Revision(CalendarItemFields Fields, ItemDetails? Details);
}
