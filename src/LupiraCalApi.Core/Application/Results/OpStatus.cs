namespace LupiraCalApi.Core.Application.Results;

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
