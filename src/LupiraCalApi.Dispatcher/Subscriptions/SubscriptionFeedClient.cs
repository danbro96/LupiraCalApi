using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LupiraCalApi.Core.Application.Subscriptions;
using LupiraCalApi.Core.Domain.Subscriptions;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Dispatcher.Subscriptions;

/// <summary>Fetches a published calendar feed conditionally. Redirects are followed here, not by the handler, so each
/// target passes <see cref="SubscriptionFeedUrl.TryNormalize"/>; <see cref="PrivateAddressGuard"/> checks every connect.</summary>
public sealed class SubscriptionFeedClient(HttpClient http, IOptions<SubscriptionOptions> options)
{
    public static readonly HttpRequestOptionsKey<bool> FeedRequest = new("lupira.subscription-feed");

    private readonly SubscriptionOptions _opts = options.Value;

    public async Task<FeedFetchResult> FetchAsync(Uri url, string? etag, DateTimeOffset? lastModified, CancellationToken ct)
    {
        // HttpClient.Timeout stops at the headers; the body read must fit in the same budget.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(_opts.Timeout);
        try
        {
            var target = url;
            for (var redirects = 0; ; redirects++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, target);
                req.Options.Set(FeedRequest, true);
                if (etag is not null && EntityTagHeaderValue.TryParse(etag, out var tag)) req.Headers.IfNoneMatch.Add(tag);
                if (lastModified is { } since) req.Headers.IfModifiedSince = since;

                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, budget.Token);
                var status = (int) resp.StatusCode;
                if (status is >= 300 and < 400 && resp.StatusCode != HttpStatusCode.NotModified)
                {
                    if (resp.Headers.Location is not { } location || redirects >= _opts.MaxRedirects) return FeedFetchResult.Failed(SubscriptionError.Unreachable);
                    if (!SubscriptionFeedUrl.TryNormalize(new Uri(target, location).AbsoluteUri, out var next, out _)) return FeedFetchResult.Failed(SubscriptionError.Blocked);
                    target = next;
                    continue;
                }

                if (resp.StatusCode == HttpStatusCode.NotModified) return FeedFetchResult.NotModified;
                if (status is >= 400 and < 500) return FeedFetchResult.Failed(SubscriptionError.Rejected);
                if (!resp.IsSuccessStatusCode) return FeedFetchResult.Failed(SubscriptionError.Unreachable);
                if (resp.Content.Headers.ContentLength > _opts.MaxBytes) return FeedFetchResult.Failed(SubscriptionError.TooLarge);

                return await ReadCappedAsync(resp.Content, budget.Token) is { } body
                    ? FeedFetchResult.Fetched(body, resp.Headers.ETag?.ToString(), resp.Content.Headers.LastModified)
                    : FeedFetchResult.Failed(SubscriptionError.TooLarge);
            }
        }
        catch (HttpRequestException ex) when (ex.GetBaseException() is FeedBlockedException)
        {
            return FeedFetchResult.Failed(SubscriptionError.Blocked);
        }
        catch (HttpRequestException)
        {
            return FeedFetchResult.Failed(SubscriptionError.Unreachable);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return FeedFetchResult.Failed(SubscriptionError.Unreachable);
        }
    }

    private async Task<string?> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > _opts.MaxBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }
}
