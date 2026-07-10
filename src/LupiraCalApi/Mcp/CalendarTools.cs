using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Calendars;
using LupiraCalApi.Dtos.Relations;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace LupiraCalApi.Mcp;

/// <summary>
/// The agent's MCP tool surface, mounted at /mcp. Each tool resolves the caller via <see cref="CurrentUser"/>
/// and delegates to the same Core services as REST, so results are scoped to the member's accessible containers.
/// Non-Ok outcomes surface as a structured <see cref="McpException"/> tool error.
/// </summary>
[McpServerToolType]
public sealed class CalendarTools
{
    [McpServerTool, Description("Search calendar items the caller can access, optionally by text and/or time window.")]
    public static async Task<IReadOnlyList<CalendarItemOccurrenceDto>> search_items(
        CalendarItemService items, CurrentUser user,
        [Description("Free-text query over title/description.")] string? query = null,
        [Description("Window start, ISO 8601.")] DateTimeOffset? from = null,
        [Description("Window end, ISO 8601.")] DateTimeOffset? to = null,
        [Description("Restrict to one calendar id.")] Guid? calendarId = null,
        [Description("Filter to items carrying this tag.")] string? tag = null)
    {
        var u = await user.GetAsync();
        return Require(await items.SearchAsync(u.Id, query, from, to, calendarId, tag));
    }

    [McpServerTool, Description("Create a calendar item; file it into a calendar (CalendarId) or leave it unfiled for curation. Category is the event type (General, Meeting, Appointment, Meal, Occasion, Outing, Trip, Stay, Activity, Focus, Chore). Details are composable: Booking (provider/confirmation/reference/amount/partySize) attaches to any category; Travel (Mode + ToPlace/FromPlace labels) applies to a Trip and requires ToPlace. A presence/availability segment uses the top-level Availability field. Bills and deliveries are LupiraTasks tasks, not calendar items — link them with link_item_to_task.")]
    public static async Task<CalendarItemDto> create_item(CalendarItemService items, CurrentUser user, CreateCalendarItemRequest request)
    {
        var u = await user.GetAsync();
        return Require(await items.CreateAsync(u.Id, request));
    }

    [McpServerTool, Description("Update a calendar item: any subset of title, description, location (free text → place), status, times, recurrence, tags, Category, and composable Details (Booking, Travel). Omitted fields are unchanged; a supplied details member replaces that member wholesale (resend the full member; Travel requires ToPlace and applies only to Category 'Trip'). Changing Category drops the previous details. The top-level Availability field sets the presence segment's status.")]
    public static async Task<CalendarItemDto> update_item(CalendarItemService items, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId, UpdateCalendarItemRequest request)
    {
        var u = await user.GetAsync();
        return Require(await items.UpdateAsync(u.Id, itemId, request));
    }

    [McpServerTool, Description("Merge an arbitrary JSON object of metadata into a calendar item.")]
    public static async Task<CalendarItemDto> attach_metadata(
        CalendarItemService items, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId,
        [Description("A JSON object of metadata keys to merge.")] string metadataJson)
    {
        var u = await user.GetAsync();
        var node = JsonNode.Parse(metadataJson) ?? new JsonObject();
        return Require(await items.AttachMetadataAsync(u.Id, itemId, node));
    }

    [McpServerTool, Description("Invite a contact to a calendar item as an attendee. role = chair|req-participant|opt-participant|non-participant (default req-participant).")]
    public static async Task<CalendarItemDto> invite_participant(
        ParticipationService participation, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId,
        [Description("The LupiraContactApi contact id to invite.")] Guid contactId,
        [Description("chair|req-participant|opt-participant|non-participant.")] string? role = null)
    {
        var u = await user.GetAsync();
        return Require(await participation.InviteAsync(u.Id, itemId, contactId, role));
    }

    [McpServerTool, Description("Record an attendee's RSVP. status = needs-action|accepted|declined|tentative|delegated.")]
    public static async Task<CalendarItemDto> respond_participant(
        ParticipationService participation, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId,
        [Description("The participation id (from the item's attendees).")] Guid participationId,
        [Description("needs-action|accepted|declined|tentative|delegated.")] string? status = null)
    {
        var u = await user.GetAsync();
        return Require(await participation.RespondAsync(u.Id, itemId, participationId, status));
    }

    [McpServerTool, Description("List the calendars the caller can access.")]
    public static async Task<IReadOnlyList<ContainerDto>> list_calendars(CalendarService calendars, CurrentUser user)
    {
        var u = await user.GetAsync();
        return Require(await calendars.ListContainersAsync(u.Id));
    }

    [McpServerTool, Description("Ensure the caller has the standard calendar set (idempotent); returns it.")]
    public static async Task<IReadOnlyList<ContainerDto>> bootstrap_me(CalendarService calendars, CurrentUser user)
    {
        var u = await user.GetAsync();
        return Require(await calendars.BootstrapPersonalAsync(u.Id));
    }

    [McpServerTool, Description("Create a calendar. Slug required.")]
    public static async Task<ContainerDto> create_calendar(CalendarService calendars, CurrentUser user, CreateCalendarRequest request)
    {
        var u = await user.GetAsync();
        return Require(await calendars.CreateAsync(u.Id, request));
    }

    [McpServerTool, Description("Grant a member access to a calendar, by email. access = owner|read-write|read (default owner).")]
    public static async Task<OwnerGrantDto> grant_calendar_owner(
        CalendarService calendars, CurrentUser user,
        [Description("Calendar id.")] Guid calendarId,
        [Description("The member's login email.")] string email,
        [Description("owner|read-write|read.")] string access = "owner")
    {
        var u = await user.GetAsync();
        return Require(await calendars.GrantCalendarOwnerAsync(u.Id, calendarId, new GrantOwnerRequest { Email = email, Access = access }));
    }

    [McpServerTool, Description("Revoke a member's access to a calendar, by email. Fails if it would remove the last owner.")]
    public static async Task<string> revoke_calendar_owner(
        CalendarService calendars, CurrentUser user,
        [Description("Calendar id.")] Guid calendarId,
        [Description("The member's login email.")] string email)
    {
        var u = await user.GetAsync();
        Require(await calendars.RevokeCalendarOwnerAsync(u.Id, calendarId, email));
        return $"Revoked {email}'s access to calendar {calendarId}.";
    }

    [McpServerTool, Description("Link a calendar item to an external item (e.g. a LupiraTasks item) by reference id.")]
    public static async Task<RelationDto> link_item_to_task(
        RelationService relations, CurrentUser user,
        [Description("Calendar item id.")] Guid itemId,
        [Description("The LupiraTasks item id.")] string taskId,
        [Description("Relation type, e.g. 'derived-from'.")] string relationType = "derived-from")
    {
        var u = await user.GetAsync();
        return Require(await relations.LinkItemAsync(u.Id, itemId, new CreateRelationRequest { ToKind = "task", ToRef = taskId, RelationType = relationType }));
    }

    [McpServerTool, Description("Find calendar items the caller can access that are linked to a given LupiraTasks item.")]
    public static async Task<IReadOnlyList<CalendarItemDto>> find_items_linked_to_task(
        RelationService relations, CurrentUser user,
        [Description("The LupiraTasks item id.")] string taskId)
    {
        var u = await user.GetAsync();
        return Require(await relations.FindItemsLinkedToAsync(u.Id, "task", taskId));
    }

    /// <summary>Unwraps a service outcome to its value, surfacing non-Ok statuses as an MCP tool error.</summary>
    private static T Require<T>(OpResult<T> r) => r.Status switch
    {
        OpStatus.Ok => r.Value!,
        OpStatus.NotFound => throw new McpException("Not found."),
        OpStatus.Forbidden => throw new McpException(r.Error ?? "Forbidden."),
        OpStatus.Invalid => throw new McpException(r.Error ?? "Invalid request."),
        OpStatus.Conflict => throw new McpException(r.Error ?? "Conflict."),
        _ => throw new McpException("Unexpected result."),
    };

    /// <summary>Asserts a no-content outcome succeeded, surfacing non-Ok statuses as an MCP tool error.</summary>
    private static void Require(OpResult r)
    {
        if (r.IsOk) return;
        throw r.Status switch
        {
            OpStatus.NotFound => new McpException("Not found."),
            OpStatus.Forbidden => new McpException(r.Error ?? "Forbidden."),
            OpStatus.Invalid => new McpException(r.Error ?? "Invalid request."),
            OpStatus.Conflict => new McpException(r.Error ?? "Conflict."),
            _ => new McpException("Unexpected result."),
        };
    }
}
