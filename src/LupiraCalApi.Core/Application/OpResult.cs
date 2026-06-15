namespace LupiraCalApi.Application;

/// <summary>
/// The transport-neutral outcome of a service operation. Each surface's adapter maps it to its own wire
/// shape (REST → <c>TypedResults</c> via <c>OpResultMap</c>; DAV → raw status codes; MCP → a tool result or
/// <c>McpException</c>). Expected outcomes are values, not exceptions. <see cref="OpStatus.Conflict"/> carries
/// the DAV If-Match / If-None-Match precondition failure (→ 412 on DAV, 409 on REST).
/// </summary>
public enum OpStatus
{
    Ok,
    NotFound,
    Forbidden,
    Invalid,
    Conflict,
}

/// <summary>A value-returning operation outcome.</summary>
public readonly record struct OpResult<T>(OpStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == OpStatus.Ok;

    public static OpResult<T> Ok(T value) => new(OpStatus.Ok, value, null);
    public static OpResult<T> NotFound() => new(OpStatus.NotFound, default, null);
    public static OpResult<T> Forbidden(string error) => new(OpStatus.Forbidden, default, error);
    public static OpResult<T> Invalid(string error) => new(OpStatus.Invalid, default, error);
    public static OpResult<T> Conflict(string error) => new(OpStatus.Conflict, default, error);
}

/// <summary>A no-content operation outcome (e.g. delete).</summary>
public readonly record struct OpResult(OpStatus Status, string? Error)
{
    public bool IsOk => Status == OpStatus.Ok;

    public static OpResult Ok() => new(OpStatus.Ok, null);
    public static OpResult NotFound() => new(OpStatus.NotFound, null);
    public static OpResult Forbidden(string error) => new(OpStatus.Forbidden, error);
    public static OpResult Invalid(string error) => new(OpStatus.Invalid, error);
    public static OpResult Conflict(string error) => new(OpStatus.Conflict, error);
}
