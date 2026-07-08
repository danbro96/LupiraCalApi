using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Places;
using LupiraCalApi.Mappers;
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

    // The catalog is shared (deduped globally by name), so any authenticated principal may read it.
    public async Task<OpResult<PlaceDto>> GetAsync(Guid id, CancellationToken ct = default) =>
        await session.LoadAsync<Place>(id, ct) is { } place
            ? OpResult<PlaceDto>.Ok(place.ToResponse())
            : OpResult<PlaceDto>.NotFound();

    /// <summary>Batch lookup; unknown ids are omitted.</summary>
    public async Task<OpResult<List<PlaceDto>>> GetManyAsync(Guid[] ids, CancellationToken ct = default)
    {
        var places = await session.Query<Place>().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        return OpResult<List<PlaceDto>>.Ok(places.Select(p => p.ToResponse()).ToList());
    }

    /// <summary>Browse/search the catalog by name (case-insensitive contains), kind, and/or parent (for the hierarchy tree).</summary>
    public async Task<OpResult<List<PlaceDto>>> SearchAsync(string? search, PlaceKind? kind, Guid? parentPlaceId, CancellationToken ct = default)
    {
        IQueryable<Place> q = session.Query<Place>();
        if (kind is { } k) q = q.Where(p => p.Kind == k);
        if (parentPlaceId is { } pid) q = q.Where(p => p.ParentPlaceId == pid);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        var places = await q.OrderBy(p => p.Name).Take(200).ToListAsync(ct);
        return OpResult<List<PlaceDto>>.Ok(places.Select(p => p.ToResponse()).ToList());
    }
}
