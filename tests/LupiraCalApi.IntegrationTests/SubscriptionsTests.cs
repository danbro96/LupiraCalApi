using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.Calendars;
using LupiraCalApi.Core.Dtos.Subscriptions;
using Npgsql;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Managing a calendar's subscriptions over REST: owners manage, any grant reads, and the feed address never
/// comes back.</summary>
public sealed class SubscriptionsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";
    private const string SecretUrl = "https://calendar.google.com/calendar/ical/alice%40x.test/private-0123456789abcdef/basic.ics";

    [Fact]
    public async Task Owner_creates_lists_updates_and_deletes()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);

        var created = await CreatedAsync(api, cal, "webcal://calendar.google.com/calendar/ical/x/basic.ics");
        Assert.Equal(cal, created.CalendarId);
        Assert.Equal("Klubben", created.DisplayName);
        Assert.Equal(ItemColor.Teal, created.Color);
        Assert.Equal("calendar.google.com", created.SourceHost);
        Assert.True(created.NextRefreshAt <= DateTimeOffset.UtcNow);
        Assert.Null(created.LastAttemptAt);
        Assert.Null(created.LastSyncedAt);
        Assert.Null(created.LastError);
        Assert.Equal(0, created.EventCount);

        Assert.Equal([created.Id], (await api.GetFromJsonAsync<List<SubscriptionDto>>(Base(cal)))!.Select(s => s.Id));

        var renamed = await api.PutAsJsonAsync($"{Base(cal)}/{created.Id}", new UpdateSubscriptionRequest { DisplayName = "Skolan", Color = null });
        renamed.EnsureSuccessStatusCode();
        var dto = (await renamed.Content.ReadFromJsonAsync<SubscriptionDto>())!;
        Assert.Equal("Skolan", dto.DisplayName);
        Assert.Null(dto.Color);
        Assert.Equal("calendar.google.com", dto.SourceHost);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"{Base(cal)}/{created.Id}")).StatusCode);
        Assert.Empty((await api.GetFromJsonAsync<List<SubscriptionDto>>(Base(cal)))!);
        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteAsync($"{Base(cal)}/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_new_feed_address_restarts_the_sync()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var sub = await CreatedAsync(api, cal);
        await ExecAsync(
            "update cal_subscriptions.subscription set last_attempt_at = now(), last_synced_at = now(), last_error = 'Rejected', consecutive_failures = 3, feed_etag = '\"x\"', content_hash = 'h', next_refresh_at = now() + interval '1 day' where id = @id",
            sub.Id);

        var resp = await api.PutAsJsonAsync($"{Base(cal)}/{sub.Id}", new UpdateSubscriptionRequest { DisplayName = "Klubben", FeedUrl = "https://outlook.office365.com/owa/calendar/x/calendar.ics" });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<SubscriptionDto>())!;

        Assert.Equal("outlook.office365.com", dto.SourceHost);
        Assert.Null(dto.LastAttemptAt);
        Assert.Null(dto.LastSyncedAt);
        Assert.Null(dto.LastError);
        Assert.True(dto.NextRefreshAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Responses_carry_the_host_never_the_feed_address()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var created = await CreateAsync(api, cal);
        var listed = await api.GetAsync(Base(cal));

        foreach (var body in new[] { await created.Content.ReadAsStringAsync(), await listed.Content.ReadAsStringAsync() })
        {
            Assert.Contains("calendar.google.com", body);
            Assert.DoesNotContain("private-0123456789abcdef", body);
            Assert.DoesNotContain("feedUrl", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_read_grant_lists_but_only_an_owner_changes()
    {
        var alice = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(alice);
        var sub = await CreatedAsync(alice, cal);
        (await alice.PostAsJsonAsync($"/calendars/{cal}/owners", new GrantOwnerRequest { Email = "bob@x.test", Access = "read" })).EnsureSuccessStatusCode();
        (await alice.PostAsJsonAsync($"/calendars/{cal}/owners", new GrantOwnerRequest { Email = "dave@x.test", Access = "read-write" })).EnsureSuccessStatusCode();
        var bob = Factory.ApiClient("bob@x.test");

        Assert.Single((await bob.GetFromJsonAsync<List<SubscriptionDto>>(Base(cal)))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateAsync(bob, cal)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync($"{Base(cal)}/{sub.Id}", new UpdateSubscriptionRequest { DisplayName = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsync($"{Base(cal)}/{sub.Id}/refresh", null)).StatusCode);
        var denied = await bob.DeleteAsync($"{Base(cal)}/{sub.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("Only an owner may change a calendar's subscriptions.", await denied.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await CreateAsync(Factory.ApiClient("dave@x.test"), cal)).StatusCode);
        var carol = Factory.ApiClient("carol@x.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await carol.GetAsync(Base(cal))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(Base(Guid.NewGuid()))).StatusCode);
    }

    [Theory]
    [InlineData("ftp://feeds.example.com/a.ics", "Klubben")]
    [InlineData("https://user:pw@feeds.example.com/a.ics", "Klubben")]
    [InlineData("https://feeds.example.com/a.ics", "  ")]
    public async Task Invalid_address_or_name_is_a_400(string url, string name)
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);

        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(api, cal, url, name)).StatusCode);
    }

    [Fact]
    public async Task A_calendar_holds_at_most_the_configured_number_of_subscriptions()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        for (var i = 0; i < 20; i++)
        {
            await CreatedAsync(api, cal, $"https://feeds.example.com/{i}.ics");
        }

        var resp = await CreateAsync(api, cal, "https://feeds.example.com/21.ics");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("at most 20", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Deleting_a_subscription_deletes_its_events()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var start = DateTimeOffset.UtcNow.AddDays(3);
        var sub = await SubscriptionSeed.SeedAsync(Factory, cal, SubscriptionSeed.Feed(SubscriptionSeed.Event("a", "A", start), SubscriptionSeed.Event("b", "B", start)));
        Assert.Equal(2, await CountEventsAsync(sub.Id));

        (await api.DeleteAsync($"{Base(cal)}/{sub.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(0, await CountEventsAsync(sub.Id));
    }

    [Fact]
    public async Task Refresh_schedules_a_fetch_unless_one_was_just_tried()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var sub = await CreatedAsync(api, cal);
        await ExecAsync("update cal_subscriptions.subscription set last_attempt_at = now(), next_refresh_at = now() + interval '1 hour' where id = @id", sub.Id);

        var cooling = (await (await api.PostAsync($"{Base(cal)}/{sub.Id}/refresh", null)).Content.ReadFromJsonAsync<SubscriptionDto>())!;
        Assert.True(cooling.NextRefreshAt > DateTimeOffset.UtcNow.AddMinutes(30));

        await ExecAsync("update cal_subscriptions.subscription set last_attempt_at = now() - interval '2 minutes' where id = @id", sub.Id);
        var refreshed = (await (await api.PostAsync($"{Base(cal)}/{sub.Id}/refresh", null)).Content.ReadFromJsonAsync<SubscriptionDto>())!;
        Assert.True(refreshed.NextRefreshAt <= DateTimeOffset.UtcNow);
    }

    private static string Base(Guid calendarId) => $"/calendars/{calendarId}/subscriptions";

    private static Task<HttpResponseMessage> CreateAsync(HttpClient api, Guid calendarId, string url = SecretUrl, string name = "Klubben") =>
        api.PostAsJsonAsync(Base(calendarId), new CreateSubscriptionRequest { FeedUrl = url, DisplayName = name, Color = ItemColor.Teal });

    private static async Task<SubscriptionDto> CreatedAsync(HttpClient api, Guid calendarId, string url = SecretUrl)
    {
        var resp = await CreateAsync(api, calendarId, url);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SubscriptionDto>())!;
    }

    private async Task ExecAsync(string sql, Guid id)
    {
        await using var conn = new NpgsqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> CountEventsAsync(Guid subscriptionId)
    {
        await using var conn = new NpgsqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select count(*) from cal_subscriptions.subscribed_event where subscription_id = @id", conn);
        cmd.Parameters.AddWithValue("id", subscriptionId);
        return (long) (await cmd.ExecuteScalarAsync())!;
    }
}
