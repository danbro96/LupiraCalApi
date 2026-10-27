using LupiraCalApi.Core.Domain.Subscriptions;

namespace LupiraCalApi.Dispatcher.Subscriptions;

/// <summary>One feed fetch: unchanged since the validators sent, a fresh body with its validators, or why it failed.</summary>
public sealed record FeedFetchResult(FeedFetchOutcome Outcome, string? Body = null, string? Etag = null, DateTimeOffset? LastModified = null, SubscriptionError? Error = null)
{
    public static readonly FeedFetchResult NotModified = new(FeedFetchOutcome.NotModified);

    public static FeedFetchResult Fetched(string body, string? etag, DateTimeOffset? lastModified) => new(FeedFetchOutcome.Fetched, body, etag, lastModified);

    public static FeedFetchResult Failed(SubscriptionError error) => new(FeedFetchOutcome.Failed, Error: error);
}
