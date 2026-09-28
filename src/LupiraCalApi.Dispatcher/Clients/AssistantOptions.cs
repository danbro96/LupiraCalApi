namespace LupiraCalApi.Dispatcher.Clients;

/// <summary>
/// Binds <c>Assistant</c> — the worker → assistant hop (fire push to <c>POST /fires</c>). Service-authed:
/// Authentik client-credentials in prod (<see cref="TokenUrl"/> + client id/secret), a dev service id header locally.
/// </summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>The assistant base address, e.g. <c>https://assistant-api.lupira.com/</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string? TokenUrl { get; set; }

    /// <summary>The assistant service client, <c>lupira-assistant-svc</c>; its token's audience is the same id.</summary>
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Optional scope to request; empty for the assistant service client.</summary>
    public string? Scope { get; set; }

    public string? DevServiceId { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
