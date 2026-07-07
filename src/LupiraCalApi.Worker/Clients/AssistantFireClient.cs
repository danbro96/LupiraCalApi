using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LupiraCalApi.Worker.Dtos;

namespace LupiraCalApi.Worker.Clients;

/// <summary>Outcome of one push. Retryable = transient (assistant down, 5xx, timeout); non-retryable = the request
/// itself is bad (400) and re-sending the same body can never succeed.</summary>
public sealed record PushResult(bool Accepted, bool Retryable, string? Error, bool Duplicate = false);

/// <summary>Pushes a claimed fire to assistant-api <c>POST /fires</c> (accept-then-own: a 202 transfers ownership;
/// re-pushes dedupe server-side on the dedupe key).</summary>
public sealed class AssistantFireClient(HttpClient http, ServiceTokenProvider tokens)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<PushResult> PostFireAsync(FireRequest fire, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "fires")
            {
                Content = JsonContent.Create(fire, options: Json),
            };
            foreach (var (key, value) in await tokens.ResolveAuthHeadersAsync(ct))
                req.Headers.TryAddWithoutValidation(key, value);

            using var resp = await http.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.Accepted)
            {
                var body = await resp.Content.ReadFromJsonAsync<FireAcceptedResponse>(Json, ct);
                return new PushResult(true, false, null, body?.Duplicate ?? false);
            }

            var error = $"assistant /fires returned {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync(ct)}";
            return new PushResult(false, Retryable: resp.StatusCode != HttpStatusCode.BadRequest, error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new PushResult(false, true, ex.Message);
        }
    }
}
