using LupiraCalApi.Domain;

namespace LupiraCalApi.Worker.Dtos;

/// <summary>The <c>POST /fires</c> wire body. assistant-api mirrors <see cref="ItemPrompt"/>/<see cref="ItemAction"/>
/// verbatim, so the Domain records serialize straight through; enums go as strings. Exactly one of
/// <see cref="Prompt"/>/<see cref="Action"/> is set.</summary>
public sealed class FireRequest
{
    public required string PrincipalId { get; set; }
    public required Guid ItemId { get; set; }
    public required Guid CalendarId { get; set; }
    public required CalendarClass CalendarClass { get; set; }
    public required CalendarKind CalendarKind { get; set; }
    public required DateTimeOffset OccurrenceAt { get; set; }
    public required string DedupeKey { get; set; }
    public DateTimeOffset? ExpireAfter { get; set; }
    public ItemPrompt? Prompt { get; set; }
    public ItemAction? Action { get; set; }
}
