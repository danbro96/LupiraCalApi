namespace LupiraCalApi.Clients;

/// <summary>What <see cref="OutboundAuthProvider"/> needs to authenticate one outbound hop: the target
/// <see cref="Audience"/> for member token exchange, and the client-credentials fallback for service calls.</summary>
public interface IOutboundHopOptions
{
    string? Audience { get; }

    string? TokenUrl { get; }

    string? ClientId { get; }

    string? ClientSecret { get; }

    string? Scope { get; }

    string? DevUser { get; }
}
