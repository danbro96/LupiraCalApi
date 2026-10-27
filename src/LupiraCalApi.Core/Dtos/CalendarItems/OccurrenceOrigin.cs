namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Ties a projected occurrence back to the entity it was derived from: <see cref="SourceId"/> is the contact
/// whose birthday it is, or the subscription whose read-only feed event it is.</summary>
public sealed class OccurrenceOrigin
{
    public required OriginKind Kind { get; set; }

    public required Guid SourceId { get; set; }
}
