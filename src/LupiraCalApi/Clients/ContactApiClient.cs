using System.Text.Json;
using LupiraCalApi.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Clients;

/// <summary>HTTP <see cref="IContactResolver"/> against LupiraContactApi's member-facing <c>POST /contacts/lookup</c> and
/// <c>GET /contacts/birthdays</c>, authenticated per <see cref="OutboundAuthProvider"/> so contact-api's address-book
/// ACL applies to the caller. A failure returns null so a lookup never breaks a write or a search — callers fail open.</summary>
public sealed class ContactApiClient(HttpClient http, IOptions<ContactApiOptions> options, OutboundAuthProvider auth, ILogger<ContactApiClient> logger) : IContactResolver
{
    private const int LookupBatch = 100;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ContactApiOptions _opts = options.Value;

    public bool IsConfigured => _opts.IsConfigured;

    public async Task<IReadOnlyList<ContactSummary>?> ResolveAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct = default)
    {
        var found = new List<ContactSummary>(contactIds.Count);
        foreach (var chunk in contactIds.Distinct().Chunk(LookupBatch))
        {
            var refs = await SendAsync<List<ContactRefItem>>(() => new HttpRequestMessage(HttpMethod.Post, "contacts/lookup")
            {
                Content = JsonContent.Create(new LookupRequest { ContactIds = [.. chunk] }, options: Json),
            }, $"lookup of {chunk.Length} ids", ct);
            if (refs is null) return null;
            found.AddRange(refs.Select(c => new ContactSummary(c.ContactId, c.DisplayName)));
        }

        return found;
    }

    public async Task<IReadOnlyList<ContactBirthday>?> BirthdaysAsync(CancellationToken ct = default)
    {
        var items = await SendAsync<List<ContactBirthdayItem>>(() => new HttpRequestMessage(HttpMethod.Get, "contacts/birthdays"), "birthdays", ct);
        return items is null ? null : [.. items.Select(c => new ContactBirthday(c.ContactId, c.DisplayName, c.Year, c.Month, c.Day))];
    }

    private async Task<T?> SendAsync<T>(Func<HttpRequestMessage> build, string what, CancellationToken ct) where T : class
    {
        try
        {
            var headers = await auth.ResolveHeadersAsync(_opts, ct);
            if (headers is null) return null;

            using var req = build();
            foreach (var (key, value) in headers)
                req.Headers.TryAddWithoutValidation(key, value);

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Contact {What} returned {Status}.", what, (int) resp.StatusCode);
                return null;
            }

            return await resp.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Contact {What} failed.", what);
            return null;
        }
    }

    private sealed class LookupRequest
    {
        public required List<Guid> ContactIds { get; set; }
    }

    private sealed class ContactRefItem
    {
        public Guid ContactId { get; set; }

        public string DisplayName { get; set; } = string.Empty;
    }

    private sealed class ContactBirthdayItem
    {
        public Guid ContactId { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public int? Year { get; set; }

        public int Month { get; set; }

        public int Day { get; set; }
    }
}
