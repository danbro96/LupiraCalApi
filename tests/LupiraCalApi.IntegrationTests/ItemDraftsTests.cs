using System.Net;
using System.Net.Http.Json;
using System.Text;
using Lupira.Testing.Postgres;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using Marten;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class ItemDraftsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";

    private const string File =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\n" +
        "BEGIN:VEVENT\r\nUID:choir@x\r\nDTSTART;TZID=Europe/Stockholm:20261019T180000\r\nDTEND;TZID=Europe/Stockholm:20261019T200000\r\n" +
        "SUMMARY:Choir\r\nRRULE:FREQ=WEEKLY;BYDAY=MO\r\nSTATUS:CONFIRMED\r\nLOCATION:Kyrkan\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nUID:choir@x\r\nRECURRENCE-ID;TZID=Europe/Stockholm:20261026T180000\r\nDTSTART;TZID=Europe/Stockholm:20261026T190000\r\nSUMMARY:Choir\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nUID:xmas@x\r\nDTSTART;VALUE=DATE:20261224\r\nDTEND;VALUE=DATE:20261226\r\nSUMMARY:Jul\r\nEND:VEVENT\r\n" +
        "END:VCALENDAR\r\n";

    private const string FloatingFile =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\n" +
        "BEGIN:VEVENT\r\nUID:fika@x\r\nDTSTART:20260701T100000\r\nSUMMARY:Fika\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nUID:call@x\r\nDTSTART;TZID=America/New_York:20260701T100000\r\nSUMMARY:Call\r\nEND:VEVENT\r\n" +
        "END:VCALENDAR\r\n";

    private static Task<HttpResponseMessage> PostFileAsync(HttpClient client, string body, string mediaType = "text/calendar", string? zone = null) =>
        client.PostAsync(zone is null ? "/items/drafts" : $"/items/drafts?zone={Uri.EscapeDataString(zone)}", new StringContent(body, Encoding.UTF8, mediaType));

    [Fact]
    public async Task A_calendar_file_reads_into_drafts_and_saves_nothing()
    {
        var resp = await PostFileAsync(Factory.ApiClient(Email), File);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var drafts = (await resp.Content.ReadFromJsonAsync<List<ItemDraftDto>>())!;
        Assert.Equal(2, drafts.Count);
        Assert.All(drafts, d => Assert.StartsWith("import-", d.SourceKey));

        var choir = drafts[0];
        Assert.Equal("Choir", choir.Title);
        Assert.Equal(ItemStatus.Confirmed, choir.Status);
        Assert.False(choir.IsAllDay);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 16, 0, 0, TimeSpan.Zero), choir.StartsAt);
        Assert.Equal("Europe/Stockholm", choir.StartTimezone);
        Assert.Equal("FREQ=WEEKLY;BYDAY=MO", choir.RecurrenceRule);
        Assert.Equal("Kyrkan", choir.Location);

        var xmas = drafts[1];
        Assert.True(xmas.IsAllDay);
        Assert.Equal(new DateOnly(2026, 12, 24), xmas.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 25), xmas.EndDate);
        Assert.Null(xmas.StartTimezone);

        await using var q = Store.QuerySession();
        Assert.Equal(0, await q.Query<CalendarItem>().CountAsync());
        Assert.Equal(0, await q.Events.QueryAllRawEvents().CountAsync());
    }

    private static async Task<CalendarItemDto> CreateFromDraftAsync(HttpClient api, Guid calId, ItemDraftDto draft)
    {
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = calId, SourceKey = draft.SourceKey, Title = draft.Title, IsAllDay = draft.IsAllDay,
            StartDate = draft.StartDate, EndDate = draft.EndDate,
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    private async Task<(HttpClient Api, Guid CalendarId, ItemDraftDto Xmas)> ImportAsync(string email)
    {
        var api = Factory.ApiClient(email);
        var calId = await CreateCalendarAsync(api);
        var drafts = await (await PostFileAsync(api, File, "text/plain")).Content.ReadFromJsonAsync<List<ItemDraftDto>>();
        return (api, calId, drafts![1]);
    }

    [Fact]
    public async Task The_same_caller_importing_twice_gets_one_item()
    {
        var (api, calId, xmas) = await ImportAsync(Email);
        var (_, _, again) = await ImportAsync(Email);

        var first = await CreateFromDraftAsync(api, calId, xmas);
        var second = await CreateFromDraftAsync(api, calId, again);

        Assert.Equal(xmas.SourceKey, again.SourceKey);
        Assert.StartsWith("import-", xmas.SourceKey);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task Two_callers_importing_the_same_file_get_two_items()
    {
        var alice = await ImportAsync(Email);
        var bob = await ImportAsync("bob@x.test");

        var aliceItem = await CreateFromDraftAsync(alice.Api, alice.CalendarId, alice.Xmas);
        var bobItem = await CreateFromDraftAsync(bob.Api, bob.CalendarId, bob.Xmas);

        Assert.NotEqual(aliceItem.Id, bobItem.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Api.GetAsync($"/items/{aliceItem.Id}")).StatusCode);
    }

    [Fact]
    public async Task Floating_times_read_in_the_given_zone_and_zoned_times_keep_theirs()
    {
        var resp = await PostFileAsync(Factory.ApiClient(Email), FloatingFile, zone: "Europe/Stockholm");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var drafts = (await resp.Content.ReadFromJsonAsync<List<ItemDraftDto>>())!;
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero), drafts[0].StartsAt);
        Assert.Equal("Europe/Stockholm", drafts[0].StartTimezone);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 14, 0, 0, TimeSpan.Zero), drafts[1].StartsAt);
        Assert.Equal("America/New_York", drafts[1].StartTimezone);
    }

    [Fact]
    public async Task An_unknown_zone_is_a_bad_request() =>
        Assert.Equal(HttpStatusCode.BadRequest, (await PostFileAsync(Factory.ApiClient(Email), FloatingFile, zone: "Mars/Olympus")).StatusCode);

    [Fact]
    public async Task A_malformed_file_is_a_bad_request()
    {
        var resp = await PostFileAsync(Factory.ApiClient(Email), "not a calendar");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Another_media_type_is_unsupported() =>
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await PostFileAsync(Factory.ApiClient(Email), File, "application/json")).StatusCode);

    [Fact]
    public async Task A_file_over_the_cap_is_too_large()
    {
        var resp = await PostFileAsync(Factory.ApiClient(Email), File + new string('x', 5 * 1024 * 1024));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resp.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_are_unauthorized() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostFileAsync(Factory.CreateClient(), File)).StatusCode);
}
