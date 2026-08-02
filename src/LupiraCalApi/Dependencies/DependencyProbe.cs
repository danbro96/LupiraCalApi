using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace LupiraCalApi.Dependencies;

/// <summary>One edge probe: mint (or reuse) a client-credentials token, GET the target's /pingz,
/// map the outcome. Uses its own named client so probe traffic never rides the real clients.</summary>
public sealed class DependencyProbe(IHttpClientFactory httpFactory)
{
    public const string ProbeClientName = "depz-probe";

    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresAt)> _tokens = new();

    public async Task<DependencyDto> ProbeAsync(DependencyTarget target, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(target.BaseUrl))
            return Result(target, DependencyStatus.Unconfigured, error: "no base URL configured");

        var client = httpFactory.CreateClient(ProbeClientName);
        var baseUrl = target.BaseUrl.EndsWith('/') ? target.BaseUrl : target.BaseUrl + "/";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(baseUrl), target.ProbePath));

        if (!string.IsNullOrWhiteSpace(target.TokenUrl) && !string.IsNullOrWhiteSpace(target.ClientId)
            && !string.IsNullOrWhiteSpace(target.ClientSecret))
        {
            try
            {
                request.Headers.Authorization = new("Bearer", await MintAsync(target, client, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result(target, DependencyStatus.NoCredential, error: $"token mint failed: {ex.Message}");
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

    private async Task<string> MintAsync(DependencyTarget target, HttpClient client, CancellationToken ct)
    {
        if (_tokens.TryGetValue(target.Name, out var cached) && DateTimeOffset.UtcNow < cached.ExpiresAt)
            return cached.Token;

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = target.ClientId!,
            ["client_secret"] = target.ClientSecret!,
        };
        // The scope pulls in the audience mapping; binding it on the provider alone is not enough.
        if (!string.IsNullOrWhiteSpace(target.Scope)) form["scope"] = target.Scope!;

        using var response = await client.PostAsync(target.TokenUrl, new FormUrlEncodedContent(form), ct);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var token = payload.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("token response had no access_token");
        var expiresIn = payload.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 300;
        _tokens[target.Name] = (token, DateTimeOffset.UtcNow.AddSeconds(expiresIn - 30));
        return token;
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
