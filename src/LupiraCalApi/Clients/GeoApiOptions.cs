namespace LupiraCalApi.Clients;

/// <summary>
/// Binds <c>Geo</c> — the cal → LupiraGeoApi hop (resolve a free-text location to a shared place). Member requests
/// exchange the caller's bearer for <see cref="Audience"/>; calls with no member (the DAV seam) use client credentials
/// (<see cref="TokenUrl"/> + client id/secret); Development sends <c>X-Dev-User</c>. Unset <see cref="BaseUrl"/> ⇒ not
/// configured ⇒ free-text locations resolve to no place id (label = raw text).
/// </summary>
public sealed class GeoApiOptions : IOutboundHopOptions
{
    public const string SectionName = "Geo";

    /// <summary>The geo base address, e.g. <c>https://geo-api.lupira.com/</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Target client_id for member token exchange (<c>lupira-geo</c>).</summary>
    public string? Audience { get; set; }

    public string? TokenUrl { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Scope for the client-credentials token — the scope mapping that injects <c>aud=lupira-geo</c>.</summary>
    public string? Scope { get; set; }

    /// <summary>Local-only: the <c>X-Dev-User</c> email to send when geo runs in Development (no Authentik).</summary>
    public string? DevUser { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
