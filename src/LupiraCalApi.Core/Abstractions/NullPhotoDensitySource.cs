namespace LupiraCalApi.Core.Abstractions;

/// <summary>Default when LupiraPhotoApi isn't configured: no photo signal.</summary>
public sealed class NullPhotoDensitySource : IPhotoDensitySource
{
    public Task<IReadOnlyList<PhotoDensityCell>?> CellsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PhotoDensityCell>?>(null);
}
