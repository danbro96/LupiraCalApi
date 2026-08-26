namespace LupiraCalApi.Dispatcher.Clients;

/// <summary>Outcome of one push. Retryable = transient (assistant down, 5xx, timeout); non-retryable = the request
/// itself is bad (400) and re-sending the same body can never succeed.</summary>
public sealed record PushResult(bool Accepted, bool Retryable, string? Error, bool Duplicate = false);
