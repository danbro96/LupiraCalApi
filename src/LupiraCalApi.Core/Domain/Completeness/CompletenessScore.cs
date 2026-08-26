namespace LupiraCalApi.Core.Domain.Completeness;

/// <summary>How well-documented a record is: <c>Score</c> 0..1 (Σ weight·presence / Σ weight), the unmet
/// fields ranked by missing mass (weight·absence, largest first), and the rubric version that produced it.
/// <c>null</c> (not this type) means "not applicable".</summary>
public sealed record CompletenessScore(double Score, int RubricVersion, IReadOnlyList<CompletenessGap> Gaps);
