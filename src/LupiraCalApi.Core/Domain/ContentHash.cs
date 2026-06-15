using System.Security.Cryptography;
using System.Text;

namespace LupiraCalApi.Domain;

/// <summary>Strong content validator: hash of the canonical bytes we hand back. Emitted to DAV clients as the ETag.</summary>
public static class ContentHash
{
    public static string Of(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
