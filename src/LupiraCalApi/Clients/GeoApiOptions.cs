namespace LupiraCalApi.Clients;

/// <summary>
/// Binds <c>Geo</c> — the cal → LupiraGeoApi hop (resolve a free-text location to a shared place). Service-authed:
/// Authentik client-credentials in prod (<see cref="TokenUrl"/> + client id/secret, audience <c>lupira-geo</c>), or an
/// <c>X-Dev-User</c> header locally. Unset <see cref="BaseUrl"/> ⇒ not configured ⇒ PlaceService uses the legacy local catalog.
/// </summary>
public sealed class GeoApiOptions
{
    public const string SectionName = "Geo";

    /// <summary>The geo base address, e.g. <c>https://geo-api.lupira.com/</c>.</summary>
    public string BaseUrl { get; set; } = "";

    public string? TokenUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Scope to request on the client-credentials token — the Authentik scope mapping that injects
    /// <c>aud=lupira-geo</c> (bound on the provider AND requested here, or geo rejects the token). See the geo
    /// deployment runbook, Authentik Part 2b.</summary>
    public string? Scope { get; set; }

    /// <summary>Local-only: the <c>X-Dev-User</c> email to send when geo runs in Development (no Authentik).</summary>
    public string? DevUser { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
