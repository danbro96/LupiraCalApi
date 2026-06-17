using LupiraCalApi.Domain;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>Resolves free-text locations (from REST input or a parsed ICS/vCard) to a reusable <see cref="Place"/> node,
/// de-duping by name. Stores into the caller's session (no SaveChanges) so it commits in the same transaction.</summary>
public sealed class PlaceService(IDocumentSession session)
{
    /// <summary>Resolve a free-text label to a (deduped) <see cref="Place"/>; returns null for empty input.</summary>
    public async Task<Guid?> ResolveLabelAsync(string? label, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var name = label.Trim();
        var existing = await session.Query<Place>().FirstOrDefaultAsync(p => p.Name == name, ct);
        if (existing is not null) return existing.Id;
        var place = new Place { Id = Guid.NewGuid(), Kind = PlaceKind.Venue, Name = name };
        session.Store(place);
        return place.Id;
    }

    /// <summary>The display name of a place (for ICS LOCATION generation), or null.</summary>
    public async Task<string?> LabelOfAsync(Guid? placeId, CancellationToken ct = default) =>
        placeId is { } id ? (await session.LoadAsync<Place>(id, ct))?.Name : null;
}
