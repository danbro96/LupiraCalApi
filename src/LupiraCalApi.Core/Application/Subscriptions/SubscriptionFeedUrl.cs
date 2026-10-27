using System.Diagnostics.CodeAnalysis;

namespace LupiraCalApi.Core.Application.Subscriptions;

/// <summary>Validates a feed address as typed or pasted by a member, and on every redirect the feed answers with.</summary>
public static class SubscriptionFeedUrl
{
    public const int MaxLength = 2048;

    public static bool TryNormalize(string? raw, [NotNullWhen(true)] out Uri? url, [NotNullWhen(false)] out string? error)
    {
        url = null;
        var text = raw?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            error = "FeedUrl is required.";
            return false;
        }

        if (text.Length > MaxLength)
        {
            error = $"FeedUrl must be at most {MaxLength} characters.";
            return false;
        }

        foreach (var scheme in (string[]) ["webcal://", "webcals://"])
        {
            if (text.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) text = "https://" + text[scheme.Length..];
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https") || parsed.Host.Length == 0)
        {
            error = "FeedUrl must be an http, https or webcal address.";
            return false;
        }

        if (parsed.UserInfo.Length > 0)
        {
            error = "FeedUrl must not contain a user name or password.";
            return false;
        }

        url = parsed;
        error = null;
        return true;
    }

    public static string SourceHost(Uri url) => url.IdnHost;
}
