using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Dependencies;

/// <summary>Gates /depz on the shared <c>X-Probe-Key</c>; a blank configured key rejects everything.</summary>
public sealed class ProbeKeyFilter(IOptions<DepzOptions> options) : IEndpointFilter
{
    public const string HeaderName = "X-Probe-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configured = options.Value.ProbeKey;
        var presented = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(configured)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(configured), Encoding.UTF8.GetBytes(presented)))
            return TypedResults.Unauthorized();
        return await next(context);
    }
}
