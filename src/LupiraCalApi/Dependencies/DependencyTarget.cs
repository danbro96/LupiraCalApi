using LupiraCalApi.Clients;

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

/// <summary>Roster derived from the same options the real clients bind — edges cannot drift.</summary>
public static class DependencyTargets
{
    public static IReadOnlyList<DependencyTarget> From(GeoApiOptions geo, ContactApiOptions contacts) =>
    [
        new DependencyTarget
        {
            Name = "lupira-geo-api",
            BaseUrl = geo.BaseUrl,
            ProbePath = "pingz",
            TokenUrl = geo.TokenUrl,
            ClientId = geo.ClientId,
            ClientSecret = geo.ClientSecret,
            Scope = geo.Scope,
            DevUser = geo.DevUser,
        },
        new DependencyTarget
        {
            Name = "lupira-contact-api",
            BaseUrl = contacts.BaseUrl,
            ProbePath = "pingz",
            TokenUrl = contacts.TokenUrl,
            ClientId = contacts.ClientId,
            ClientSecret = contacts.ClientSecret,
            Scope = contacts.Scope,
            DevUser = contacts.DevUser,
        },
    ];
}
