namespace LupiraCalApi.Clients;

/// <summary>
/// Binds <c>Contacts</c> — the cal → LupiraContactApi hop (validate contact ids referenced by attendees and item
/// details, list birthdays), authenticated per <see cref="OutboundAuthProvider"/>. Unset <see cref="BaseUrl"/> ⇒ not
/// configured ⇒ contact refs are stored unvalidated.
/// </summary>
public sealed class ContactApiOptions : IOutboundHopOptions
{
    public const string SectionName = "Contacts";

    /// <summary>The contact base address, e.g. <c>http://lupira-contact-api:8080/</c> (in-network hop).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Target client_id for member token exchange (<c>lupira-contact</c>).</summary>
    public string? Audience { get; set; }

    public string? TokenUrl { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Scope for the client-credentials token — the scope mapping that injects <c>aud=lupira-contact</c>.</summary>
    public string? Scope { get; set; }

    /// <summary>Local-only: the <c>X-Dev-User</c> email to send when contact-api runs in Development (no Authentik).</summary>
    public string? DevUser { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
