namespace LupiraCalApi.Core.Application;

/// <summary>A resolved contact reference: the id exists and is live in LupiraContactApi.</summary>
public sealed record ContactSummary(Guid ContactId, string DisplayName);
