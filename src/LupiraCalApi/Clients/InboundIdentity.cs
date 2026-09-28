namespace LupiraCalApi.Clients;

/// <summary>Who is behind the current request, as far as outbound auth cares.</summary>
public enum InboundIdentity
{
    /// <summary>No member behind the call (the DAV gateway, background work, anonymous).</summary>
    Service,

    /// <summary>A member authenticated by an Authentik bearer.</summary>
    Member,

    /// <summary>A member authenticated by the Development-only <c>X-Dev-User</c> header.</summary>
    DevMember,
}
