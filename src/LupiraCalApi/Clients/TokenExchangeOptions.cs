namespace LupiraCalApi.Clients;

/// <summary>
/// Binds <c>Auth:Exchange</c> — cal-api as the RFC 8693 requester. The member's inbound bearer (issued by the
/// <c>lupira-cal</c> provider) is exchanged for a token scoped to the target's audience, authenticated with this
/// confidential client. The target providers federate <c>lupira-cal</c>.
/// </summary>
public sealed class TokenExchangeOptions
{
    public const string SectionName = "Auth:Exchange";

    public string? TokenUrl { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string Scope { get; set; } = "openid profile email";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TokenUrl) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
