namespace LupiraCalApi.Core.Abstractions;

/// <summary>
/// The caller's photos aggregated into ~100 m cells — LupiraPhotoApi owns photos and scopes them to their owner.
/// Implemented over HTTP by the host; the no-op default (<see cref="NullPhotoDensitySource"/>) keeps hotspots events-only.
/// </summary>
public interface IPhotoDensitySource
{
    /// <summary><c>null</c> = unavailable (unconfigured, transport or token failure); an empty list means no located photos.</summary>
    Task<IReadOnlyList<PhotoDensityCell>?> CellsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);
}
