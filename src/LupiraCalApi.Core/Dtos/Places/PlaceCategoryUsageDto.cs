using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Dtos.Places;

public sealed class PlaceCategoryUsageDto
{
    public required ItemCategory Category { get; set; }

    public required int EventCount { get; set; }

    public required DateOnly LastDay { get; set; }
}
