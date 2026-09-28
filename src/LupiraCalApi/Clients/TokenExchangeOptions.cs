namespace LupiraCalApi.Clients;

/// <summary>
/// Binds <c>Auth:Exchange</c> — cal-api's confidential client as the RFC 8693 requester for
/// <see cref="OutboundAuthProvider"/>. The target providers federate <c>lupira-cal</c>.
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
