using System.ComponentModel;
using System.Text.Json.Nodes;
using LupiraCalApi.Api;
using LupiraCalApi.Domain;
using ModelContextProtocol.Server;

namespace LupiraCalApi.Mcp;

/// <summary>
/// The agent's MCP tool surface, mounted at /api/mcp. Each tool resolves the caller via <see cref="IUserContext"/>
/// and delegates to the same services as REST, so results are scoped to the member's own + shared containers.
/// </summary>
[McpServerToolType]
public sealed class CalendarTools
{
    [McpServerTool, Description("Search calendar events the caller can access, optionally by text and/or time window.")]
    public static async Task<IReadOnlyList<EventOccurrenceDto>> search_events(
        EventService events, IUserContext user,
        [Description("Free-text query over title/description/location.")] string? query = null,
        [Description("Window start, ISO 8601.")] DateTimeOffset? from = null,
        [Description("Window end, ISO 8601.")] DateTimeOffset? to = null,
        [Description("Restrict to one calendar id.")] Guid? calendarId = null,
        [Description("Filter to events carrying this tag.")] string? tag = null,
        [Description("Filter to events whose metadata JSON contains this object, e.g. {\"trip\":\"tokyo-2026\"}.")] string? metadataContains = null)
    {
        var u = await user.GetCurrentUserAsync();
        return await events.SearchAsync(u.Id, query, from, to, calendarId, tag, metadataContains);
    }

    [McpServerTool, Description("Create a calendar event in a calendar the caller can write.")]
    public static async Task<EventDto> create_event(EventService events, IUserContext user, CreateEventRequest request)
    {
        var u = await user.GetCurrentUserAsync();
        return await events.CreateAsync(u.Id, request);
    }

    [McpServerTool, Description("Merge an arbitrary JSON object of metadata into an event.")]
    public static async Task<EventDto> attach_metadata(
        EventService events, IUserContext user,
        [Description("Event id.")] Guid eventId,
        [Description("A JSON object of metadata keys to merge.")] string metadataJson)
    {
        var u = await user.GetCurrentUserAsync();
        var node = JsonNode.Parse(metadataJson) ?? new JsonObject();
        return await events.AttachMetadataAsync(u.Id, eventId, node);
    }

    [McpServerTool, Description("Find contacts the caller can access, optionally by name/organization.")]
    public static async Task<IReadOnlyList<ContactDto>> query_contacts(
        ContactService contacts, IUserContext user,
        [Description("Free-text query over name/organization.")] string? query = null)
    {
        var u = await user.GetCurrentUserAsync();
        return await contacts.QueryAsync(u.Id, query, null);
    }

    [McpServerTool, Description("List the calendars and address books the caller can access.")]
    public static async Task<IReadOnlyList<ContainerDto>> list_calendars(CalendarService calendars, IUserContext user)
    {
        var u = await user.GetCurrentUserAsync();
        return await calendars.ListContainersAsync(u.Id);
    }

    [McpServerTool, Description("Link an event to an external item (e.g. a LupiraTasks item) by reference id.")]
    public static async Task<RelationDto> link_event_to_task(
        RelationService relations, IUserContext user,
        [Description("Event id.")] Guid eventId,
        [Description("The LupiraTasks item id.")] string taskId,
        [Description("Relation type, e.g. 'derived-from'.")] string relationType = "derived-from")
    {
        var u = await user.GetCurrentUserAsync();
        return await relations.LinkEventAsync(u.Id, eventId, new CreateRelationRequest("task", taskId, relationType, null));
    }

    [McpServerTool, Description("Find events the caller can access that are linked to a given LupiraTasks item.")]
    public static async Task<IReadOnlyList<EventDto>> find_events_linked_to_task(
        RelationService relations, IUserContext user,
        [Description("The LupiraTasks item id.")] string taskId)
    {
        var u = await user.GetCurrentUserAsync();
        return await relations.FindEventsLinkedToAsync(u.Id, "task", taskId);
    }
}
