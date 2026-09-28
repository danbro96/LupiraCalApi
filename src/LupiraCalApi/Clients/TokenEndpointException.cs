namespace LupiraCalApi.Clients;

public sealed class TokenEndpointException(TokenErrorKind kind, int? statusCode, string? description)
    : Exception($"Token endpoint refused ({kind}, {statusCode?.ToString() ?? "no response"}): {description}")
{
    public TokenErrorKind Kind { get; } = kind;

    public int? StatusCode { get; } = statusCode;

    public string? Description { get; } = description;

    public static TokenErrorKind Parse(string? error) => error switch
    {
        "invalid_request" => TokenErrorKind.InvalidRequest,
        "invalid_client" => TokenErrorKind.InvalidClient,
        "invalid_grant" => TokenErrorKind.InvalidGrant,
        "invalid_scope" => TokenErrorKind.InvalidScope,
        "invalid_target" => TokenErrorKind.InvalidTarget,
        "access_denied" => TokenErrorKind.AccessDenied,
        _ => TokenErrorKind.Other,
    };
}
