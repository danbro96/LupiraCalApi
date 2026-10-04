using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lupira.Mcp;
using LupiraCalApi.Auth;
using LupiraCalApi.Core.Application.Calendars;
using LupiraCalApi.Core.Application.Hotspots;
using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Calendars;
using LupiraCalApi.Core.Dtos.Hotspots;
using LupiraCalApi.Core.Dtos.Relations;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LupiraCalApi.Mcp;

/// <summary>
/// The agent's MCP tool surface, mounted at /mcp. Each tool resolves the caller via <see cref="CurrentUser"/>
/// and delegates to the same Core services as REST, so results are scoped to the member's accessible containers.
/// Non-Ok outcomes surface as a structured <see cref="McpException"/> tool error.
/// </summary>
[McpServerToolType]
public sealed class CalendarTools
{
    [McpServerTool(Name = "search_items")]
    [Description("Search calendar items the caller can access, optionally by text, time window (matches occurrences overlapping it), category/status, tag; pageable.")]
    public static async Task<IReadOnlyList<CalendarItemOccurrenceDto>> SearchItems(
        CalendarItemService items, CurrentUser user,
        [Description("Free-text query over title/description. With no from/to, matches all-time.")] string? query = null,
        [Description("Window start, ISO 8601. Without a query, defaults to one year ago.")] DateTimeOffset? from = null,
        [Description("Window end, ISO 8601. Without a query, defaults to one year ahead; recurrences expand at most one year ahead unless set.")] DateTimeOffset? to = null,
        [Description("Restrict to one calendar id.")] Guid? calendarId = null,
        [Description("Filter to items carrying this tag.")] string? tag = null,
        [Description("Filter to child items nested under this parent item id (e.g. a trip's sub-events).")] Guid? parentId = null,
        [Description("Filter to items where this LupiraContactApi contact is an attendee. With no from/to, matches all-time.")] Guid? contactId = null,
        [Description("Filter by category (General, Meeting, Appointment, Meal, Occasion, Outing, Trip, Stay, Activity, Focus, Chore).")] string? category = null,
        [Description("Filter by status (Tentative, Confirmed, Cancelled).")] string? status = null,
        [Description("Skip this many occurrences (paging; applied after sorting).")] int? skip = null,
        [Description("Max occurrences returned (counts expanded occurrences, not items).")] int? take = null,
        [Description("Newest first (sort by occurrence start descending).")] bool desc = false)
    {
        var u = await user.GetAsync();
        return (await items.SearchAsync(u.Id, query, from, to, calendarId, tag, parentId, contactId, category, status, skip, take, desc)).Require();
    }

    [McpServerTool(Name = "create_item")]
    [Description("Create a calendar item; file it into a calendar (CalendarId) or leave it unfiled for curation. Category is the event type (General, Meeting, Appointment, Meal, Occasion, Outing, Trip, Stay, Activity, Focus, Chore). A location must be a resolved LupiraGeoApi PlaceId (resolve via lupira-geo first — free-text Location is only a label). Timed without StartTimezone ⇒ the calendar's zone, else the server default. Details are composable: Booking (provider/confirmation/reference/amount/partySize) attaches to any category; Travel (Mode + ToPlaceId, optional FromPlaceId) applies to a Trip and requires ToPlaceId. A presence/availability segment uses the top-level Availability field. For a historical/backfilled item known only to the month/year/roughly, still pass a concrete date and set StartPrecision/EndPrecision (Exact|Day|Month|Year|Approximate). Metadata (a JSON object, e.g. import provenance) can be merged inline at creation. Set a client SourceKey for idempotent re-create; nest under a parent via ParentItemId or ParentSourceKey. Bills and deliveries are LupiraTasks tasks — link them with link_item_to_task.")]
    public static async Task<CalendarItemDto> CreateItem(CalendarItemService items, CurrentUser user, CreateCalendarItemRequest request)
    {
        var u = await user.GetAsync();
        return (await items.CreateAsync(u.Id, request)).Require();
    }

    [McpServerTool(Name = "create_items_batch")]
    [Description("Create many calendar items in one call — for imports/backfills. Idempotent per item on its SourceKey (re-running returns the existing item, no duplicate). Children reference their parent by ParentSourceKey (the parent's SourceKey) in any order; the server orders parents first. Locations must be resolved PlaceIds (see create_item). Returns a per-item result {sourceKey, itemId, status: created|existed|invalid} in input order; one bad item does not fail the batch.")]
    public static async Task<IReadOnlyList<ItemBatchResult>> CreateItemsBatch(CalendarItemService items, CurrentUser user, CreateCalendarItemsBatchRequest request)
    {
        var u = await user.GetAsync();
        return (await items.CreateBatchAsync(u.Id, request.Items)).Require();
    }

    [McpServerTool(Name = "get_item")]
    [Description("Get one calendar item in full: attendees (with participationIds), details, metadata, per-occurrence changes of a recurring item. Use it to read before editing; search_items returns slim occurrences.")]
    public static async Task<CalendarItemDto> GetItem(CalendarItemService items, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId)
    {
        var u = await user.GetAsync();
        return (await items.GetAsync(u.Id, itemId)).Require();
    }

    [McpServerTool(Name = "update_item")]
    [Description("Update a calendar item: any subset of title, description, location, status, times, recurrence, tags, Category, and composable Details (Booking, Travel). To change location pass PlaceId (a resolved LupiraGeoApi place id; resolve via lupira-geo first) with Location as the display label — a free-text-only Location change is rejected; PlaceIdProvided=true with null PlaceId clears it. ParentItemId moves the item under another item (not itself); ParentItemIdProvided=true with null ParentItemId makes it top-level. Omitted fields are unchanged; a supplied details member replaces that member wholesale (resend the full member; Travel requires ToPlaceId and applies only to Category 'Trip'). Changing Category drops the previous details. The top-level Availability field sets the presence segment's status. Made timed or given a recurrence without StartTimezone ⇒ the calendar's zone, else the server default.")]
    public static async Task<CalendarItemDto> UpdateItem(CalendarItemService items, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId, UpdateCalendarItemRequest request)
    {
        var u = await user.GetAsync();
        return (await items.UpdateAsync(u.Id, itemId, request)).Require();
    }

    [McpServerTool(Name = "change_occurrence")]
    [Description("Change ONE occurrence of a recurring item, addressed by the start it has in the unmodified series (UTC). Excluded=true removes it from the series (skip); otherwise StartsAt/EndsAt/Title/Description/Status override the series for that occurrence only (omitted = inherit; Status Cancelled keeps it on record as cancelled). A move is StartsAt (+ EndsAt). Replaces any earlier change to that occurrence. Use update_item to change the whole series.")]
    public static async Task<CalendarItemDto> ChangeOccurrence(CalendarItemService items, CurrentUser user,
        [Description("Calendar item id (the recurring series).")] Guid itemId,
        [Description("The occurrence's start in the unmodified series, ISO 8601.")] DateTimeOffset originalStart,
        ChangeOccurrenceRequest request)
    {
        var u = await user.GetAsync();
        return (await items.ChangeOccurrenceAsync(u.Id, itemId, originalStart, request)).Require();
    }

    [McpServerTool(Name = "restore_occurrence")]
    [Description("Revert ONE occurrence of a recurring item to the series, dropping its exclusion or override.")]
    public static async Task<string> RestoreOccurrence(CalendarItemService items, CurrentUser user,
        [Description("Calendar item id (the recurring series).")] Guid itemId,
        [Description("The occurrence's start in the unmodified series, ISO 8601.")] DateTimeOffset originalStart)
    {
        var u = await user.GetAsync();
        (await items.RestoreOccurrenceAsync(u.Id, itemId, originalStart)).Require();
        return $"Occurrence {originalStart:O} of {itemId} follows the series.";
    }

    [McpServerTool(Name = "delete_item")]
    [Description("Delete a calendar item (soft delete + tombstone). Removes the whole item from every calendar it's filed in — not a single occurrence of a recurring series, and not just one calendar. Requires write access. Already-deleted or unknown ids return not found.")]
    public static async Task<string> DeleteItem(
        CalendarItemService items, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId)
    {
        var u = await user.GetAsync();
        (await items.DeleteAsync(u.Id, itemId)).Require();
        return $"Deleted calendar item {itemId}.";
    }

    [McpServerTool(Name = "list_items_at_place")]
    [Description("Calendar items whose location is this LupiraGeoApi place id. Answers 'what still references this place?' before merging, renaming or deleting it in the gazetteer — list_orphans gives the count, this gives the items.")]
    public static async Task<IReadOnlyList<CalendarItemDto>> ListItemsAtPlace(
        CalendarItemService items, CurrentUser user,
        [Description("LupiraGeoApi place id.")] Guid placeId)
    {
        var u = await user.GetAsync();
        return (await items.ByPlaceAsync(u.Id, placeId)).Require();
    }

    [McpServerTool(Name = "list_thin_items")]
    [Description("Check-in worklist: calendar items ranked thinnest-first by completeness score (0..1, ascending; most recent start first on ties). Item-granular — recurring items appear once. Each item carries Completeness with ranked Gaps: the fields worth asking the user about. Exempt items (system/Birthdays/Availability calendars, cancelled, presence/payload) never appear. When a gap doesn't apply (e.g. no booking for a homemade dinner), acknowledge it via attach_metadata with {\"completeness\":{\"na\":[\"booking\"]}} — the field stops counting and the ask goes away.")]
    public static async Task<IReadOnlyList<CalendarItemDto>> ListThinItems(
        CalendarItemService items, CurrentUser user,
        [Description("Restrict to one calendar id.")] Guid? calendarId = null,
        [Description("Filter by category (General, Meeting, Appointment, Meal, Occasion, Outing, Trip, Stay, Activity, Focus, Chore).")] string? category = null,
        [Description("Only items scoring strictly below this (0..1). Default 1 = any item with gaps.")] double? maxScore = null,
        [Description("Max items returned (default 25).")] int? take = null)
    {
        var u = await user.GetAsync();
        return (await items.ThinItemsAsync(u.Id, calendarId, category, maxScore, take)).Require();
    }

    [McpServerTool(Name = "attach_metadata")]
    [Description("Merge an arbitrary JSON object of metadata into a calendar item. Also the channel for completeness N/A acknowledgments: {\"completeness\":{\"na\":[\"booking\",\"seat\"]}} marks those rubric fields as not applicable so the item's completeness score stops counting them.")]
    public static async Task<CalendarItemDto> AttachMetadata(
        CalendarItemService items, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId,
        [Description("A JSON object of metadata keys to merge.")] string metadataJson)
    {
        var u = await user.GetAsync();
        return (await items.AttachMetadataAsync(u.Id, itemId, ParseMetadataObject(metadataJson))).Require();
    }

    [McpServerTool(Name = "file_item")]
    [Description("File an already-created calendar item into a calendar. status = proposed|accepted (default proposed); 'accepted' files it directly (then visible over DAV), 'proposed' queues it for accept/reject. Returns the updated item. Unknown or inaccessible items return not found.")]
    public static async Task<CalendarItemDto> FileItem(
        CurationService curation, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId,
        [Description("Target calendar id.")] Guid calendarId,
        [Description("proposed|accepted (default proposed).")] string? status = null)
    {
        var u = await user.GetAsync();
        return (await curation.AddToCalendarAsync(u.Id, itemId, calendarId, status)).Require();
    }

    [McpServerTool(Name = "file_items_batch")]
    [Description("File many existing calendar items into calendars in one call. Each entry files ItemId into CalendarId with an optional Status (proposed|accepted, default proposed) and is authorized independently — one bad entry never fails the batch. Returns a per-entry result {itemId, calendarId, status: filed|notfound|forbidden|invalid, error} in input order.")]
    public static async Task<IReadOnlyList<FileItemResult>> FileItemsBatch(
        CurationService curation, CurrentUser user, FileItemsBatchRequest request)
    {
        var u = await user.GetAsync();
        return (await curation.AddToCalendarBatchAsync(u.Id, request.Entries)).Require();
    }

    [McpServerTool(Name = "invite_participant")]
    [Description("Invite a contact to a calendar item as an attendee. role = chair|req-participant|opt-participant|non-participant (default req-participant).")]
    public static async Task<CalendarItemDto> InviteParticipant(
        ParticipationService participation, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId,
        [Description("The LupiraContactApi contact id to invite.")] Guid contactId,
        [Description("chair|req-participant|opt-participant|non-participant.")] string? role = null)
    {
        var u = await user.GetAsync();
        return (await participation.InviteAsync(u.Id, itemId, contactId, role)).Require();
    }

    [McpServerTool(Name = "respond_participant")]
    [Description("Record an attendee's RSVP. status = needs-action|accepted|declined|tentative|delegated.")]
    public static async Task<CalendarItemDto> RespondParticipant(
        ParticipationService participation, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId,
        [Description("The participation id (from the item's attendees).")] Guid participationId,
        [Description("needs-action|accepted|declined|tentative|delegated.")] string? status = null)
    {
        var u = await user.GetAsync();
        return (await participation.RespondAsync(u.Id, itemId, participationId, status)).Require();
    }

    [McpServerTool(Name = "remove_participant")]
    [Description("Remove an attendee from an item entirely — for a wrong attendee (data error). Someone who was invited but didn't come is a decline (respond_participant), not a removal.")]
    public static async Task<CalendarItemDto> RemoveParticipant(
        ParticipationService participation, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId,
        [Description("The participation id (from get_item's attendees).")] Guid participationId)
    {
        var u = await user.GetAsync();
        return (await participation.RemoveAsync(u.Id, itemId, participationId)).Require();
    }

    [McpServerTool(Name = "set_participants")]
    [Description("Add a set of contacts as attendees of an item in one call (add-only — keeps existing attendees). attended=true (default) also marks them attended, for historical/backfilled events (avoids the pending-invite look); pass false for a live invite flow. Returns a slim result (the additions + already-present count), not the full item. Prefer this over repeated invite_participant.")]
    public static async Task<SetParticipantsResult> SetParticipants(
        ParticipationService participation, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId,
        [Description("LupiraContactApi contact ids to add as attendees.")] Guid[] contactIds,
        [Description("Also mark them attended (default true; false = plain invite in NeedsAction).")] bool attended = true)
    {
        var u = await user.GetAsync();
        return (await participation.SetParticipantsAsync(u.Id, itemId, contactIds, attended)).Require();
    }

    [McpServerTool(Name = "participation_summary")]
    [Description("Per-contact participation across the caller's readable calendars: {contactId, count, lastAt, score}, ordered by score — recency-weighted interaction (each past occurrence 0.5^(age / 90 days), the next planned one 1), so people you stopped meeting fade. Use it to rank ambiguous contact matches (e.g. lupira-contact resolve_contacts candidates) by real interaction. Optional from/to restricts to occurrences in that window.")]
    public static async Task<IReadOnlyList<ParticipationSummaryEntry>> ParticipationSummary(
        ParticipationService participation, CurrentUser user,
        [Description("Window start, ISO 8601 (optional; default all-time).")] DateTimeOffset? from = null,
        [Description("Window end, ISO 8601 (optional; default all-time).")] DateTimeOffset? to = null)
    {
        var u = await user.GetAsync();
        return (await participation.SummaryAsync(u.Id, from, to)).Require();
    }

    [McpServerTool(Name = "list_calendars")]
    [Description("List the calendars the caller can access.")]
    public static async Task<IReadOnlyList<ContainerDto>> ListCalendars(CalendarService calendars, CurrentUser user)
    {
        var u = await user.GetAsync();
        return (await calendars.ListContainersAsync(u.Id)).Require();
    }

    [McpServerTool(Name = "bootstrap_me")]
    [Description("Ensure the caller has the standard calendar set (idempotent); returns it. Calendars it creates get defaultTimezone, else the server default; existing calendars are unchanged.")]
    public static async Task<IReadOnlyList<ContainerDto>> BootstrapMe(CalendarService calendars, CurrentUser user,
        [Description("IANA time zone id, e.g. Europe/Stockholm.")] string? defaultTimezone = null)
    {
        var u = await user.GetAsync();
        return (await calendars.BootstrapPersonalAsync(u.Id, defaultTimezone)).Require();
    }

    [McpServerTool(Name = "create_calendar")]
    [Description("Create a calendar. Slug required. DefaultTimezone (IANA id) defaults to the server default.")]
    public static async Task<ContainerDto> CreateCalendar(CalendarService calendars, CurrentUser user, CreateCalendarRequest request)
    {
        var u = await user.GetAsync();
        return (await calendars.CreateAsync(u.Id, request)).Require();
    }

    [McpServerTool(Name = "update_calendar")]
    [Description("Change a calendar's DefaultTimezone (IANA id, e.g. Europe/Stockholm); owner-only. Applies to items written later without a zone; existing items keep theirs.")]
    public static async Task<ContainerDto> UpdateCalendar(CalendarService calendars, CurrentUser user,
        [Description("Calendar id.")] Guid calendarId, UpdateCalendarRequest request)
    {
        var u = await user.GetAsync();
        return (await calendars.UpdateAsync(u.Id, calendarId, request)).Require();
    }

    [McpServerTool(Name = "grant_calendar_owner")]
    [Description("Grant a member access to a calendar, by email. access = owner|read-write|read (default owner).")]
    public static async Task<OwnerGrantDto> GrantCalendarOwner(
        CalendarService calendars, CurrentUser user,
        [Description("Calendar id.")] Guid calendarId,
        [Description("The member's login email.")] string email,
        [Description("owner|read-write|read.")] string access = "owner")
    {
        var u = await user.GetAsync();
        return (await calendars.GrantCalendarOwnerAsync(u.Id, calendarId, new GrantOwnerRequest { Email = email, Access = access })).Require();
    }

    [McpServerTool(Name = "revoke_calendar_owner")]
    [Description("Revoke a member's access to a calendar, by email. Fails if it would remove the last owner.")]
    public static async Task<string> RevokeCalendarOwner(
        CalendarService calendars, CurrentUser user,
        [Description("Calendar id.")] Guid calendarId,
        [Description("The member's login email.")] string email)
    {
        var u = await user.GetAsync();
        (await calendars.RevokeCalendarOwnerAsync(u.Id, calendarId, email)).Require();
        return $"Revoked {email}'s access to calendar {calendarId}.";
    }

    [McpServerTool(Name = "link_item_to_task")]
    [Description("Link a calendar item to an external item (e.g. a LupiraTasks item) by reference id.")]
    public static async Task<RelationDto> LinkItemToTask(
        RelationService relations, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId,
        [Description("The LupiraTasks item id.")] string taskId,
        [Description("Relation type, e.g. 'derived-from'.")] string relationType = "derived-from")
    {
        var u = await user.GetAsync();
        return (await relations.LinkItemAsync(u.Id, itemId, new CreateRelationRequest { ToKind = "task", ToRef = taskId, RelationType = relationType })).Require();
    }

    [McpServerTool(Name = "unlink_item_from_task")]
    [Description("Remove a calendar item's link to a LupiraTasks item. Idempotent: no matching link is not an error.")]
    public static async Task<string> UnlinkItemFromTask(
        RelationService relations, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId,
        [Description("The LupiraTasks item id.")] string taskId,
        [Description("Relation type, e.g. 'derived-from'.")] string relationType = "derived-from")
    {
        var u = await user.GetAsync();
        (await relations.UnlinkItemBatchAsync(u.Id, itemId, new DeleteRelationsBatchRequest { ToKind = "task", RelationType = relationType, ToRefs = [taskId] })).Require();
        return $"Unlinked calendar item {itemId} from task {taskId}.";
    }

    [McpServerTool(Name = "list_items_linked_to_task")]
    [Description("Find calendar items the caller can access that are linked to a given LupiraTasks item.")]
    public static async Task<IReadOnlyList<CalendarItemDto>> ListItemsLinkedToTask(
        RelationService relations, CurrentUser user,
        [Description("The LupiraTasks item id.")] string taskId)
    {
        var u = await user.GetAsync();
        return (await relations.FindItemsLinkedToAsync(u.Id, "task", taskId)).Require();
    }

    [McpServerTool(Name = "list_hotspots")]
    [Description("Places where the caller's events and photos concentrate, ranked by active days (distinct UTC days with an event occurrence or a photo there). Events come from calendars the caller can read, photos are their own. Each hotspot carries the LupiraGeoApi PlaceId it anchors to, else a reverse-geocoded label.")]
    public static async Task<IReadOnlyList<HotspotDto>> ListHotspots(
        HotspotService hotspots, CurrentUser user,
        [Description("Window start, ISO 8601. Default: all-time.")] DateTimeOffset? from = null,
        [Description("Window end, ISO 8601. Default: now.")] DateTimeOffset? to = null,
        [Description("Count events from this calendar id only.")] Guid? calendarId = null,
        [Description("Minimum active days for a hotspot (default 3).")] int? minDays = null,
        [Description("Max hotspots returned (default 100, max 500).")] int? limit = null)
    {
        var u = await user.GetAsync();
        return (await hotspots.ListAsync(u.Id, from, to, calendarId, minDays, limit)).Require();
    }

    private static JsonObject ParseMetadataObject(string metadataJson)
    {
        try
        {
            if (JsonNode.Parse(metadataJson) is JsonObject obj) return obj;
        }
        catch (JsonException)
        {
        }
        throw new McpException("`metadataJson` must be a JSON object.");
    }
}
