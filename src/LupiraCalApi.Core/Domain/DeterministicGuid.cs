using System.Security.Cryptography;
using System.Text;

namespace LupiraCalApi.Domain;

/// <summary>Stable Guid derived from a natural key (an iCal/vCard uid) — so a DELETE-then-PUT of the same uid lands on the same event stream and resurrects it rather than creating a duplicate.</summary>
public static class DeterministicGuid
{
    public static Guid From(string value) => new(MD5.HashData(Encoding.UTF8.GetBytes(value)));
}
