using LupiraCalApi.Clients;

namespace LupiraCalApi.Dependencies;

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
