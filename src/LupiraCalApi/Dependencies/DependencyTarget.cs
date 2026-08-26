namespace LupiraCalApi.Dependencies;

/// <summary>One outward edge: where, and the client-credentials to probe it as (mirrors the real
/// clients' auth — creds → bearer, DevUser → X-Dev-User, else anonymous).</summary>
public sealed class DependencyTarget
{
    public required string Name { get; set; }
    public required string BaseUrl { get; set; }
    public required string ProbePath { get; set; }
    public string? TokenUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Scope { get; set; }
    public string? DevUser { get; set; }
}
