namespace LupiraCalApi.Worker.Clients;

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
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? DevServiceId { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
