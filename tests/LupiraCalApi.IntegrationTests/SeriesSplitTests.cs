using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Lupira.Contracts.Fires;
using Lupira.Primitives;
using Lupira.Testing.Postgres;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Relations;
using ModelContextProtocol.Client;
using Npgsql;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Changing an occurrence and every one after it: the series ends before it and a continuing series takes the rest
/// with the item's calendars, attendees, metadata, payload and relations; replays land on the same series; at the first
/// occurrence the whole series changes; stale or invalid requests write nothing.</summary>
public sealed class SeriesSplitTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";

    private static DateTimeOffset Utc(int m, int d, int h) => new(2026, m, d, h, 0, 0, TimeSpan.Zero);

    // Sundays 18:00 Stockholm: 16:00Z under summer time, 17:00Z from 2026-10-25.
    private static async Task<CalendarItemDto> CreateWeeklyAsync(HttpClient api, Guid cal, string? rule = null)
    {
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = cal, Title = "Middag", IsAllDay = false, StartsAt = Utc(10, 4, 16), EndsAt = Utc(10, 4, 19), StartTimezone = "Europe/Stockholm",
            RecurrenceRule = rule ?? "FREQ=WEEKLY;BYDAY=SU", Tags = ["family"], Metadata = new JsonObject { ["source"] = "test" },
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    private static Task<HttpResponseMessage> SplitAsync(HttpClient api, Guid itemId, DateTimeOffset at, ChangeFollowingOccurrencesRequest body, Guid? key = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/items/{itemId}/occurrences/{at:yyyy-MM-ddTHH:mm:ssZ}/following") { Content = JsonContent.Create(body) };
        if (key is { } k) req.Headers.Add("Idempotency-Key", k.ToString());
        return api.SendAsync(req);
    }

    private static async Task<SeriesSplitDto> SplitOkAsync(HttpClient api, Guid itemId, DateTimeOffset at, ChangeFollowingOccurrencesRequest body, Guid? key = null)
    {
        var resp = await SplitAsync(api, itemId, at, body, key);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SeriesSplitDto>())!;
    }

    private static async Task<List<CalendarItemOccurrenceDto>> OccurrencesAsync(HttpClient api) =>
        (await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items/?from=2026-10-01T00:00:00Z&to=2026-11-16T00:00:00Z"))!;

    [Fact]
    public async Task The_rest_continues_as_a_new_series_carrying_the_item_along()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var item = await CreateWeeklyAsync(api, cal);
        (await api.PutAsJsonAsync($"/items/{item.Id}/occurrences/2026-10-11T16:00:00Z", new ChangeOccurrenceRequest { Title = "Hos mormor" })).EnsureSuccessStatusCode();
        (await api.PutAsJsonAsync($"/items/{item.Id}/occurrences/2026-10-25T17:00:00Z", new ChangeOccurrenceRequest { Excluded = true })).EnsureSuccessStatusCode();
        var contact = Guid.NewGuid();
        var invited = await (await api.PostAsync($"/items/{item.Id}/participants?contactId={contact}", null)).Content.ReadFromJsonAsync<CalendarItemDto>();
        var participation = Assert.Single(invited!.Attendees).ParticipationId;
        (await api.PostAsync($"/items/{item.Id}/participants/{participation}/respond?status=accepted", null)).EnsureSuccessStatusCode();
        (await api.PutAsync($"/items/{item.Id}/participants/{participation}/reminder?mode=Before&minutesBefore=30", null)).EnsureSuccessStatusCode();
        (await api.PostAsJsonAsync($"/items/{item.Id}/relations", new CreateRelationRequest { ToKind = "task", ToRef = "t-1", RelationType = "related-to" })).EnsureSuccessStatusCode();

        var split = await SplitOkAsync(api, item.Id, Utc(10, 18, 16),
            new ChangeFollowingOccurrencesRequest { Changes = new UpdateCalendarItemRequest { Title = "Middag (ny)" }, SourceKey = "split-1" });

        var earlier = split.Earlier!;
        var following = split.Following!;
        Assert.Equal(item.Id, earlier.Id);
        Assert.Equal("FREQ=WEEKLY;BYDAY=SU;UNTIL=20261011T160000Z", earlier.RecurrenceRule);
        Assert.Equal(Utc(10, 11, 16), Assert.Single(earlier.OccurrenceOverrides!).OriginalStart);
        Assert.Null(earlier.ExcludedOccurrences);
        Assert.Equal(DeterministicGuid.From("split-1"), following.Id);
        Assert.Equal(item.Id, following.ContinuesItemId);
        Assert.Equal(("Middag (ny)", Utc(10, 18, 16), Utc(10, 18, 19)), (following.Title, following.StartsAt!.Value, following.EndsAt!.Value));
        Assert.Equal("FREQ=WEEKLY;BYDAY=SU", following.RecurrenceRule);
        Assert.Equal([Utc(10, 25, 17)], following.ExcludedOccurrences!);
        Assert.Equal(["family"], following.Tags!);
        Assert.Equal("test", following.Metadata["source"]!.GetValue<string>());
        Assert.Equal(cal, Assert.Single(following.Calendars).CalendarId);
        var attendee = Assert.Single(following.Attendees);
        Assert.Equal(SeriesSplit.ContinuedParticipationId(following.Id, participation), attendee.ParticipationId);
        Assert.Equal((contact, ParticipationStatus.Accepted, 30), (attendee.ContactId, attendee.Status, attendee.Reminder!.MinutesBefore!.Value));
        var relations = await api.GetFromJsonAsync<List<RelationDto>>($"/items/{following.Id}/relations");
        Assert.Equal("t-1", Assert.Single(relations!).ToRef);

        var occ = await OccurrencesAsync(api);
        Assert.Equal(
            [(item.Id, Utc(10, 4, 16), "Middag"), (item.Id, Utc(10, 11, 16), "Hos mormor"), (following.Id, Utc(10, 18, 16), "Middag (ny)"),
                (following.Id, Utc(11, 1, 17), "Middag (ny)"), (following.Id, Utc(11, 8, 17), "Middag (ny)"), (following.Id, Utc(11, 15, 17), "Middag (ny)")],
            occ.Select(o => (o.Id, o.Start, o.Title!)));

        var uids = await ListBackendUidsAsync(api, Email, cal);
        Assert.Contains(item.ExternalId, uids);
        Assert.Contains("split-1", uids);
        Assert.Contains("UNTIL=20261011T160000Z", await (await GetIcsBackendAsync(api, Email, cal, item.ExternalId)).Content.ReadAsStringAsync());
        Assert.Contains("DTSTART;TZID=Europe/Stockholm:20261018T180000", await (await GetIcsBackendAsync(api, Email, cal, "split-1")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_withdrawn_attendee_does_not_continue()
    {
        var api = Factory.ApiClient(Email);
        var item = await CreateWeeklyAsync(api, await CreateCalendarAsync(api));
        var (staying, leaving) = (Guid.NewGuid(), Guid.NewGuid());
        (await api.PostAsync($"/items/{item.Id}/participants?contactId={staying}", null)).EnsureSuccessStatusCode();
        var invited = await (await api.PostAsync($"/items/{item.Id}/participants?contactId={leaving}", null)).Content.ReadFromJsonAsync<CalendarItemDto>();
        var left = invited!.Attendees.Single(a => a.ContactId == leaving).ParticipationId;
        (await api.PostAsync($"/items/{item.Id}/participants/{left}/leave", null)).EnsureSuccessStatusCode();

        var split = await SplitOkAsync(api, item.Id, Utc(10, 18, 16), new ChangeFollowingOccurrencesRequest { Changes = new UpdateCalendarItemRequest { Title = "Ny" } });

        Assert.Equal(staying, Assert.Single(split.Following!.Attendees).ContactId);
        Assert.NotNull(split.Earlier!.Attendees.Single(a => a.ContactId == leaving).LeftAt);
    }

    [Fact]
    public async Task A_replay_lands_on_the_same_series_and_writes_nothing()
    {
        var api = Factory.ApiClient(Email);
        var item = await CreateWeeklyAsync(api, await CreateCalendarAsync(api));
        var key = Guid.NewGuid();
        var body = new ChangeFollowingOccurrencesRequest { Changes = new UpdateCalendarItemRequest { Title = "Ny" } };

        var first = await SplitOkAsync(api, item.Id, Utc(10, 18, 16), body, key);
        var replay = await SplitOkAsync(api, item.Id, Utc(10, 18, 16), body, key);
        var keyed = await SplitOkAsync(api, item.Id, Utc(10, 18, 16), new ChangeFollowingOccurrencesRequest { Changes = body.Changes, SourceKey = $"{key:N}@cal.lupira.com" });

        Assert.Equal(DeterministicGuid.From($"{key:N}@cal.lupira.com"), first.Following!.Id);
        foreach (var again in new[] { replay, keyed })
        {
            Assert.Equal((first.Earlier!.Id, first.Earlier.Version), (again.Earlier!.Id, again.Earlier.Version));
            Assert.Equal((first.Following.Id, first.Following.Version), (again.Following!.Id, again.Following.Version));
        }
    }

    [Fact]
    public async Task At_the_first_occurrence_the_whole_series_changes_or_goes()
    {
        var api = Factory.ApiClient(Email);
        var item = await CreateWeeklyAsync(api, await CreateCalendarAsync(api));

        var changed = await SplitOkAsync(api, item.Id, Utc(10, 4, 16), new ChangeFollowingOccurrencesRequest { Changes = new UpdateCalendarItemRequest { Title = "Ny" } });
        Assert.Null(changed.Earlier);
        Assert.Equal((item.Id, "Ny", "FREQ=WEEKLY;BYDAY=SU"), (changed.Following!.Id, changed.Following.Title, changed.Following.RecurrenceRule));

        var gone = await SplitOkAsync(api, item.Id, Utc(10, 4, 16), new ChangeFollowingOccurrencesRequest { Excluded = true });
        Assert.Equal((null, null), (gone.Earlier, gone.Following));
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/items/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_following_occurrences_ends_the_series_before_them()
    {
        var api = Factory.ApiClient(Email);
        var item = await CreateWeeklyAsync(api, await CreateCalendarAsync(api), "FREQ=WEEKLY;BYDAY=SU;COUNT=6");

        var split = await SplitOkAsync(api, item.Id, Utc(10, 18, 16), new ChangeFollowingOccurrencesRequest { Excluded = true });

        Assert.Null(split.Following);
        Assert.Equal("FREQ=WEEKLY;BYDAY=SU;COUNT=2", split.Earlier!.RecurrenceRule);
        Assert.Equal([Utc(10, 4, 16), Utc(10, 11, 16)], (await OccurrencesAsync(api)).Select(o => o.Start));
    }

    private const string SeriesWithExtra = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//lupira-test//EN\r\nBEGIN:VEVENT\r\nUID:extra@x\r\n" +
        "DTSTART:20261005T090000Z\r\nDTEND:20261005T100000Z\r\nSUMMARY:Standup\r\nRRULE:FREQ=WEEKLY\r\nRDATE:20261007T120000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    [Fact]
    public async Task Invalid_forbidden_unknown_and_stale_requests_write_nothing()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var item = await CreateWeeklyAsync(api, cal);
        var change = new ChangeFollowingOccurrencesRequest { Changes = new UpdateCalendarItemRequest { Title = "Ny" } };
        Assert.Equal(HttpStatusCode.Created, (await PutIcsBackendAsync(api, Email, cal, "extra@x", SeriesWithExtra)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await SplitAsync(api, DeterministicGuid.From("extra@x"), Utc(10, 7, 12), change)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SplitAsync(api, item.Id, Utc(10, 19, 16), change)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SplitAsync(api, item.Id, Utc(10, 18, 16), new ChangeFollowingOccurrencesRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SplitAsync(api, Guid.NewGuid(), Utc(10, 18, 16), change)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SplitAsync(Factory.ApiClient("bob@x.test"), item.Id, Utc(10, 18, 16), change)).StatusCode);

        var revisedAt = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        (await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest { Description = "Söndagsmiddag", OccurredAt = revisedAt })).EnsureSuccessStatusCode();
        var stale = await SplitAsync(api, item.Id, Utc(10, 18, 16),
            new ChangeFollowingOccurrencesRequest { Changes = change.Changes, OccurredAt = revisedAt.AddHours(-1), SourceKey = "stale" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var other = await CreateWeeklyAsync(api, cal);
        var taken = await SplitAsync(api, item.Id, Utc(10, 18, 16), new ChangeFollowingOccurrencesRequest { Changes = change.Changes, SourceKey = other.ExternalId });
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/items/{DeterministicGuid.From("stale")}")).StatusCode);
        Assert.Equal("FREQ=WEEKLY;BYDAY=SU", (await api.GetFromJsonAsync<CalendarItemDto>($"/items/{item.Id}"))!.RecurrenceRule);
    }

    [Fact]
    public async Task Fires_follow_the_split()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var start = new DateTimeOffset(DateTimeOffset.UtcNow.Date.AddDays(2), TimeSpan.Zero).AddHours(9);
        var created = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = cal, Title = "Check-in", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC", RecurrenceRule = "FREQ=WEEKLY",
        });
        var item = (await created.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        (await api.PutAsJsonAsync($"/items/{item.Id}/prompt", new SetItemPromptRequest
        {
            Intent = PromptIntent.Monitor, Instruction = "check in", Output = OutputKind.Summary, Fire = new PromptFire(PromptFireKind.OnStart, null, null),
            Target = new Ref(RefKind.Event, item.Id, null),
        })).EnsureSuccessStatusCode();

        var split = await SplitOkAsync(api, item.Id, start.AddDays(14), new ChangeFollowingOccurrencesRequest { Changes = new UpdateCalendarItemRequest() });
        using (var daemon = await Store.BuildProjectionDaemonAsync())
            await daemon.RebuildProjectionAsync("scheduled_fire", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Equal([start, start.AddDays(7)], await FiresAsync(item.Id));
        Assert.Equal([start.AddDays(14), start.AddDays(21), start.AddDays(28)], await FiresAsync(split.Following!.Id));
        Assert.Equal(split.Following.Id, split.Following.Prompt!.Target!.Id);
    }

    [Fact]
    public async Task The_agent_changes_following_occurrences_over_mcp()
    {
        var api = Factory.ApiClient(Email);
        var item = await CreateWeeklyAsync(api, await CreateCalendarAsync(api));
        var http = Factory.ApiClient(Email);
        await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, ownsHttpClient: true));

        var result = await mcp.CallToolAsync("change_following_occurrences", new Dictionary<string, object?>
        {
            ["itemId"] = item.Id,
            ["originalStart"] = "2026-10-18T16:00:00Z",
            ["request"] = new Dictionary<string, object?> { ["changes"] = new Dictionary<string, object?> { ["title"] = "Ny" }, ["sourceKey"] = "mcp-split" },
        });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("FREQ=WEEKLY;BYDAY=SU;UNTIL=20261011T160000Z", (await api.GetFromJsonAsync<CalendarItemDto>($"/items/{item.Id}"))!.RecurrenceRule);
        Assert.Equal("Ny", (await api.GetFromJsonAsync<CalendarItemDto>($"/items/{DeterministicGuid.From("mcp-split")}"))!.Title);
    }

    private async Task<List<DateTimeOffset>> FiresAsync(Guid itemId)
    {
        await using var conn = new NpgsqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select occurrence_at from cal.scheduled_fire where item_id = @id order by occurrence_at", conn);
        cmd.Parameters.AddWithValue("id", itemId);
        var fires = new List<DateTimeOffset>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) fires.Add(new DateTimeOffset(reader.GetDateTime(0).ToUniversalTime(), TimeSpan.Zero));
        return fires;
    }
}
