using System.Diagnostics;
using LupiraCalApi.Clients;

namespace LupiraCalApi.Dependencies;

/// <summary>One edge probe: mint (or reuse) a client-credentials token, GET the target's /pingz,
/// map the outcome. Uses its own named client so probe traffic never rides the real clients.</summary>
public sealed class DependencyProbe(IHttpClientFactory httpFactory, TokenEndpointClient tokens, TokenCache cache)
{
    public const string ProbeClientName = "depz-probe";

    public async Task<DependencyDto> ProbeAsync(DependencyTarget target, CancellationToken ct)
    {
        if (!target.IsConfigured)
            return Result(target, DependencyStatus.Unconfigured, error: "no base URL configured");

        var client = httpFactory.CreateClient(ProbeClientName);
        var baseUrl = target.BaseUrl.EndsWith('/') ? target.BaseUrl : target.BaseUrl + "/";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(baseUrl), target.ProbePath));

        if (!string.IsNullOrWhiteSpace(target.TokenUrl) && !string.IsNullOrWhiteSpace(target.ClientId)
            && !string.IsNullOrWhiteSpace(target.ClientSecret))
        {
            try
            {
                var token = await cache.GetOrMintAsync(TokenCache.ClientCredentialsKey(target), token => tokens.ClientCredentialsAsync(target, token), null, ct);
                request.Headers.Authorization = new("Bearer", token);
            }
            catch (TokenEndpointException ex)
            {
                return Result(target, DependencyStatus.NoCredential,
                    error: $"token mint failed: {ex.Kind} ({ex.StatusCode?.ToString() ?? "no response"}) {ex.Description}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(target.DevUser))
        {
            request.Headers.TryAddWithoutValidation("X-Dev-User", target.DevUser);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, ct);
            stopwatch.Stop();
            var status = (int) response.StatusCode switch
            {
                >= 200 and < 300 => DependencyStatus.Healthy,
                401 or 403 => DependencyStatus.Unauthorized,
                _ => DependencyStatus.Degraded,
            };
            var error = status == DependencyStatus.Healthy ? null : $"{target.ProbePath} returned {(int) response.StatusCode}";
            return Result(target, status, stopwatch.Elapsed.TotalMilliseconds, error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            stopwatch.Stop();
            return Result(target, DependencyStatus.Down, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
        }
    }

    private static DependencyDto Result(DependencyTarget target, DependencyStatus status, double? latencyMs = null, string? error = null)
    {
        DependencyTelemetry.Record(target.Name, status, latencyMs);
        return new DependencyDto
        {
            Name = target.Name,
            Status = status,
            LatencyMs = latencyMs,
            Error = error,
            CheckedUtc = DateTimeOffset.UtcNow,
        };
    }
}
