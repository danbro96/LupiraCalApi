namespace LupiraCalApi.Clients;

/// <summary>
/// Binds <c>Photos</c> — the cal → LupiraPhotoApi hop (the caller's photo density for hotspots). Photos are owner-scoped,
/// so only member token exchange applies; no client-credentials fallback is configured. Unset <see cref="BaseUrl"/> ⇒ not
/// configured ⇒ hotspots are events-only.
/// </summary>
public sealed class PhotoApiOptions : IOutboundHopOptions
{
    public const string SectionName = "Photos";

    /// <summary>The photo base address, e.g. <c>http://lupira-photo-api:8080/</c> (in-network hop).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Target client_id for member token exchange (<c>lupira-photo</c>).</summary>
    public string? Audience { get; set; }

    public string? TokenUrl { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string? Scope { get; set; }

    /// <summary>Local-only: the <c>X-Dev-User</c> email to send when photo-api runs in Development (no Authentik).</summary>
    public string? DevUser { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
