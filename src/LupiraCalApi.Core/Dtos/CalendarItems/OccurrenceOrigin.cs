namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Ties a projected occurrence back to the entity it was derived from — the birthday occurrence's
/// <see cref="SourceId"/> is the contact whose birthday it is.</summary>
public sealed class OccurrenceOrigin
{
    public required OriginKind Kind { get; set; }
    public required Guid SourceId { get; set; }
}
