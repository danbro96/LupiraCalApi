namespace LupiraCalApi.Clients;

/// <summary>Why the token endpoint refused (RFC 6749 §5.2 / RFC 8693 <c>error</c>), or <see cref="Unavailable"/>
/// when it could not be reached or answered with a non-OAuth error.</summary>
public enum TokenErrorKind
{
    Other,
    InvalidRequest,
    InvalidClient,
    InvalidGrant,
    InvalidScope,
    InvalidTarget,
    AccessDenied,
    Unavailable,
}
