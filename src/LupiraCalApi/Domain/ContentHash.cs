using System.Security.Cryptography;
using System.Text;

namespace LupiraCalApi.Domain;

/// <summary>Strong content validator: hash of the canonical bytes we hand back. Emitted to DAV clients as the ETag.</summary>
public static class ContentHash
{
    public static string Of(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}

/// <summary>Thrown when the caller may not access (or write) a container. Mapped to 403 by the endpoint pipeline.</summary>
public sealed class AccessDeniedException(string message = "Access denied.") : Exception(message);

/// <summary>Thrown when a DAV If-Match / If-None-Match precondition fails. Mapped to 412 by the DAV layer.</summary>
public sealed class DavPreconditionException(string message = "Precondition failed.") : Exception(message);
