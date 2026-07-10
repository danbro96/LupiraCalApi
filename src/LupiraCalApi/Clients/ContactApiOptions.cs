namespace LupiraCalApi.Clients;

/// <summary>
/// Binds <c>Contacts</c> — the cal → LupiraContactApi hop (validate/resolve contact ids referenced by attendees
/// and item details). Service-authed: Authentik client-credentials in prod (<see cref="TokenUrl"/> + client
/// id/secret, audience <c>lupira-contact</c>), or an <c>X-Dev-User</c> header locally. Unset <see cref="BaseUrl"/>
/// ⇒ not configured ⇒ contact refs are stored unvalidated (today's behavior).
/// </summary>
public sealed class ContactApiOptions
{
    public const string SectionName = "Contacts";

    /// <summary>The contact base address, e.g. <c>http://lupira-contact-api:8080/</c> (in-network hop).</summary>
    public string BaseUrl { get; set; } = "";

    public string? TokenUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Scope to request on the client-credentials token — the Authentik scope mapping that injects
    /// <c>aud=lupira-contact</c> (bound on the provider AND requested here, or contact rejects the token).</summary>
    public string? Scope { get; set; }

    /// <summary>Local-only: the <c>X-Dev-User</c> email to send when contact-api runs in Development (no Authentik).</summary>
    public string? DevUser { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
