namespace LupiraCalApi.Clients;

public sealed record InboundCaller(InboundIdentity Kind, string? SubjectToken = null, DateTimeOffset? SubjectExpiresAt = null, string? DevEmail = null, string? SubjectClient = null);
