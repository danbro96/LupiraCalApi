namespace LupiraCalApi.Dispatcher.Clients;

/// <summary>
/// Binds <c>Assistant</c> — the worker → assistant hop (fire push to <c>POST /fires</c>). Service-authed:
/// Authentik client-credentials in prod (<see cref="TokenUrl"/> + client id/secret), a dev service id header locally.
/// </summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>The assistant base address, e.g. <c>https://assistant-api.lupira.com/</c>.</summary>
    public string BaseUrl { get; set; } = "";

    public string? TokenUrl { get; set; }

    /// <summary>The assistant service provider's slug, not its audience.</summary>
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Scope to request — the Authentik mapping that injects <c>aud=lupira-assistant-internal</c>.
    /// Binding it on the provider alone is not enough; assistant rejects a token without that aud.</summary>
    public string? Scope { get; set; }

    public string? DevServiceId { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
