using LupiraCalApi.Dav;
using LupiraCalApi.Dtos.Calendars;
using LupiraCalApi.Dtos.Me;
using System.Net.Http.Json;
using System.Text;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Base for integration tests: shares the container fixture, resets Marten data before each test, and
/// provides fixture + /dav-backend helpers. Lives in the "integration" collection so tests run serially against the shared DB.</summary>
[Collection("integration")]
public abstract class IntegrationTest(CalApiTestFactory factory) : IAsyncLifetime
{
    protected readonly CalApiTestFactory Factory = factory;

    public async Task InitializeAsync() => await Factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---- REST fixture helpers ----

    protected static async Task<Guid> GetMyIdAsync(HttpClient api)
    {
        var me = await api.GetFromJsonAsync<MeDto>("/me");
        return me!.Id;
    }

    protected static async Task<Guid> CreateCalendarAsync(HttpClient api, string slug = "work", string? displayName = "Work")
    {
        var resp = await api.PostAsJsonAsync("/calendars", new CreateCalendarRequest { Slug = slug, DisplayName = displayName, Type = "calendar", DefaultTimezone = "UTC" });
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<ContainerDto>();
        return dto!.Id;
    }

    // ---- /dav-backend helpers (the seam the LupiraDavApi gateway consumes) ----

    protected static string DavBackendBase(string email) => $"/dav-backend/u/{Uri.EscapeDataString(email)}";

    protected static async Task<HttpResponseMessage> PutIcsBackendAsync(
        HttpClient client, string email, Guid calId, string uid, string ics, string? ifMatch = null, bool ifNoneMatchStar = false)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put,
            $"{DavBackendBase(email)}/collections/{calId}/resources/{uid}");
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
        if (ifNoneMatchStar) req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        req.Content = new StringContent(ics, Encoding.UTF8, "text/calendar");
        return await client.SendAsync(req);
    }

    protected static Task<HttpResponseMessage> GetIcsBackendAsync(HttpClient client, string email, Guid calId, string uid) =>
        client.GetAsync($"{DavBackendBase(email)}/collections/{calId}/resources/{uid}");

    /// <summary>The uids a Depth:1 listing of the collection would enumerate.</summary>
    protected static async Task<List<string>> ListBackendUidsAsync(HttpClient client, string email, Guid calId)
    {
        var resp = await client.PostAsJsonAsync($"{DavBackendBase(email)}/collections/{calId}/query", new DavQueryRequest());
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<DavResourcesDto>();
        return [.. body!.Resources.Select(r => r.Uid)];
    }

    // ---- payload builders ----

    protected static string MinimalIcs(string uid, string summary, DateTimeOffset startUtc, string? rrule = null)
    {
        var dt = startUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
        var end = startUtc.AddHours(1).UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//lupira-test//EN\r\nBEGIN:VEVENT\r\n");
        sb.Append($"UID:{uid}\r\nSUMMARY:{summary}\r\nDTSTART:{dt}\r\nDTEND:{end}\r\n");
        if (rrule is not null) sb.Append($"RRULE:{rrule}\r\n");
        sb.Append("END:VEVENT\r\nEND:VCALENDAR\r\n");
        return sb.ToString();
    }

    protected static string MinimalIcsAllDay(string uid, string summary, DateOnly start)
    {
        var d = start.ToString("yyyyMMdd");
        var end = start.AddDays(1).ToString("yyyyMMdd");
        return $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//lupira-test//EN\r\nBEGIN:VEVENT\r\nUID:{uid}\r\nSUMMARY:{summary}\r\nDTSTART;VALUE=DATE:{d}\r\nDTEND;VALUE=DATE:{end}\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
    }
}
