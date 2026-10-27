using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Lupira.Testing.Postgres;
using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Application.Subscriptions;
using LupiraCalApi.Core.Data;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Domain.Subscriptions;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Calendars;
using LupiraCalApi.Core.Dtos.Subscriptions;
using LupiraCalApi.Dispatcher.Subscriptions;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>
/// The dispatcher's refresh loop against the real mirror, with the feeds served by an in-process
/// <see cref="HttpMessageHandler"/> stub (the upstream-stub convention), and what search then shows.
/// </summary>
public sealed class SubscriptionRefreshTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";
    private const string FeedUrl = "https://feeds.test/klubben.ics";
    private static readonly DateTimeOffset Monday = new(2026, 9, 7, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Synced_events_appear_in_search_as_read_only_subscription_occurrences()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var sub = await SubscribeAsync(api, cal);
        var feeds = new StubFeeds();
        feeds.Serve(FeedUrl, Klubben());

        Assert.Equal(1, await TickAsync(feeds));

        var status = await StatusAsync(api, cal, sub.Id);
        Assert.NotNull(status.LastSyncedAt);
        Assert.Null(status.LastError);
        Assert.Equal(2, status.EventCount);
        Assert.True(status.NextRefreshAt > DateTimeOffset.UtcNow.AddMinutes(50));

        var found = await SearchAsync(api);
        Assert.Equal(5, found.Count);
        Assert.All(found, o =>
        {
            Assert.Equal(OriginKind.Subscription, o.Origin!.Kind);
            Assert.Equal(sub.Id, o.Origin.SourceId);
            Assert.Equal([cal], o.CalendarIds);
            Assert.Equal(ItemColor.Green, o.Color);
            Assert.Equal(0, o.ChildCount);
        });
        Assert.Equal(4, found.Count(o => o.Title == "Träning"));

        var training = found.First(o => o.Title == "Träning");
        var detail = await api.GetFromJsonAsync<SubscribedEventDto>($"/calendars/{cal}/subscriptions/{sub.Id}/events/{training.Id}");
        Assert.Equal("Ta med skor", detail!.Description);
        Assert.Equal("Klubben", detail.SubscriptionName);
        Assert.True(detail.IsRecurring);
        Assert.Equal(Monday, detail.Start);
    }

    [Fact]
    public async Task Item_filters_exclude_subscribed_events_and_text_and_calendar_scope_apply()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var other = await CreateCalendarAsync(api, "other", "Other");
        await SubscribeAsync(api, cal);
        var feeds = new StubFeeds();
        feeds.Serve(FeedUrl, Klubben());
        await TickAsync(feeds);

        Assert.Empty(await SearchAsync(api, "&tag=x"));
        Assert.Empty(await SearchAsync(api, $"&contactId={Guid.NewGuid()}"));
        Assert.Empty(await SearchAsync(api, "&category=Meeting"));
        Assert.Equal(["Match"], (await SearchAsync(api, "&query=matc")).Select(o => o.Title));
        Assert.Equal(4, (await SearchAsync(api, "&query=skor")).Count);
        Assert.Equal(5, (await SearchAsync(api, $"&calendarId={cal}")).Count);
        Assert.Empty(await SearchAsync(api, $"&calendarId={other}"));
        Assert.Empty(await SearchAsync(api, "&status=Tentative"));

        Assert.Empty(await SearchAsync(Factory.ApiClient("mallory@x.test")));
        (await api.PostAsJsonAsync($"/calendars/{cal}/owners", new GrantOwnerRequest { Email = "bob@x.test", Access = "read" })).EnsureSuccessStatusCode();
        Assert.Equal(5, (await SearchAsync(Factory.ApiClient("bob@x.test"))).Count);
    }

    [Fact]
    public async Task Not_modified_or_an_identical_body_leaves_the_events_alone()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var tagged = await SubscribeAsync(api, cal);
        var plain = await SubscribeAsync(api, cal, "https://feeds.test/plain.ics");
        var feeds = new StubFeeds();
        feeds.Serve(FeedUrl, Klubben(), etag: "\"v1\"");
        feeds.Serve("https://feeds.test/plain.ics", SubscriptionSeed.Feed(SubscriptionSeed.Event("plain@x", "Plain", Monday)));
        await TickAsync(feeds);
        var before = await EventRowVersionsAsync();
        var firstSync = (await StatusAsync(api, cal, tagged.Id)).LastSyncedAt;

        await MakeDueAsync();
        Assert.Equal(2, await TickAsync(feeds));

        Assert.Equal(before, await EventRowVersionsAsync());
        Assert.Contains(feeds.Requests, r => r.RequestUri!.AbsoluteUri == FeedUrl && r.Headers.IfNoneMatch.Any(t => t.Tag == "\"v1\""));
        Assert.True((await StatusAsync(api, cal, tagged.Id)).LastSyncedAt > firstSync);
        Assert.Equal(1, (await StatusAsync(api, cal, plain.Id)).EventCount);
    }

    [Fact]
    public async Task A_failed_fetch_keeps_the_events_and_backs_off()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var sub = await SubscribeAsync(api, cal);
        var feeds = new StubFeeds();
        feeds.Serve(FeedUrl, Klubben());
        await TickAsync(feeds);

        foreach (var (respond, error, failures) in new (Func<HttpRequestMessage, HttpResponseMessage>, SubscriptionError, int)[]
        {
            (_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), SubscriptionError.Unreachable, 1),
            (_ => new HttpResponseMessage(HttpStatusCode.Forbidden), SubscriptionError.Rejected, 2),
            (_ => StubFeeds.Ok("<html><body>Sign in</body></html>"), SubscriptionError.NotACalendar, 3),
        })
        {
            feeds.Routes[FeedUrl] = respond;
            await MakeDueAsync();
            await TickAsync(feeds);

            var status = await StatusAsync(api, cal, sub.Id);
            Assert.Equal(error, status.LastError);
            Assert.True(status.NextRefreshAt > DateTimeOffset.UtcNow.AddHours((1 << (failures - 1)) - 0.1));
            Assert.Equal(2, status.EventCount);
            Assert.Equal(5, (await SearchAsync(api)).Count);
        }

        feeds.Serve(FeedUrl, Klubben());
        await MakeDueAsync();
        await TickAsync(feeds);
        Assert.Null((await StatusAsync(api, cal, sub.Id)).LastError);
    }

    [Fact]
    public async Task A_removed_event_is_deleted_and_a_changed_one_gets_a_new_etag()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var sub = await SubscribeAsync(api, cal);
        var feeds = new StubFeeds();
        feeds.Serve(FeedUrl, Klubben());
        await TickAsync(feeds);
        var before = (await SearchAsync(api)).First(o => o.Title == "Träning");

        feeds.Serve(FeedUrl, Klubben("Träning A-lag", withMatch: false));
        await MakeDueAsync();
        await TickAsync(feeds);

        var after = await SearchAsync(api);
        Assert.Equal(4, after.Count);
        Assert.All(after, o => Assert.Equal("Träning A-lag", o.Title));
        Assert.Equal(before.Id, after[0].Id);
        Assert.NotEqual(before.Etag, after[0].Etag);
        Assert.Equal(1, (await StatusAsync(api, cal, sub.Id)).EventCount);
    }

    [Fact]
    public async Task Concurrent_ticks_never_claim_the_same_subscription()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var feeds = new StubFeeds();
        for (var i = 0; i < 8; i++)
        {
            var url = $"https://feeds.test/{i}.ics";
            await SubscribeAsync(api, cal, url);
            feeds.Serve(url, SubscriptionSeed.Feed(SubscriptionSeed.Event($"e{i}", $"E{i}", Monday)));
        }

        var claimed = await Task.WhenAll(TickAsync(feeds), TickAsync(feeds));

        Assert.Equal(8, claimed.Sum());
        Assert.Equal(8, feeds.Requests.Select(r => r.RequestUri!.AbsoluteUri).Distinct().Count());
        Assert.Equal(8, feeds.Requests.Count);
        Assert.Equal(0, await TickAsync(feeds));
    }

    [Fact]
    public async Task A_feed_on_a_private_address_is_blocked()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var sub = await SubscribeAsync(api, cal, "http://127.0.0.1:9/feed.ics");

        await TickAsync(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, ConnectCallback = PrivateAddressGuard.ConnectAsync });

        Assert.Equal(SubscriptionError.Blocked, (await StatusAsync(api, cal, sub.Id)).LastError);
    }

    [Fact]
    public async Task A_redirect_to_another_scheme_is_blocked()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var sub = await SubscribeAsync(api, cal);
        var feeds = new StubFeeds();
        feeds.Routes[FeedUrl] = _ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("file:///etc/passwd") } };

        await TickAsync(feeds);

        Assert.Equal(SubscriptionError.Blocked, (await StatusAsync(api, cal, sub.Id)).LastError);
    }

    private static string Klubben(string trainingTitle = "Träning", bool withMatch = true) => SubscriptionSeed.Feed(
        SubscriptionSeed.Event("training@klubben", trainingTitle, Monday, "FREQ=WEEKLY;COUNT=4", "Ta med skor"),
        withMatch ? SubscriptionSeed.Event("match@klubben", "Match", Monday.AddDays(5)) : string.Empty);

    private static async Task<SubscriptionDto> StatusAsync(HttpClient api, Guid calendarId, Guid subscriptionId) =>
        (await api.GetFromJsonAsync<List<SubscriptionDto>>($"/calendars/{calendarId}/subscriptions"))!.Single(s => s.Id == subscriptionId);

    private static async Task<List<CalendarItemOccurrenceDto>> SearchAsync(HttpClient api, string query = "") =>
        (await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/items?from=2026-09-01T00:00:00Z&to=2026-10-01T00:00:00Z{query}"))!;

    private static async Task<SubscriptionDto> SubscribeAsync(HttpClient api, Guid calendarId, string url = FeedUrl)
    {
        var request = new CreateSubscriptionRequest { FeedUrl = url, DisplayName = "Klubben", Color = ItemColor.Green };
        var resp = await api.PostAsJsonAsync($"/calendars/{calendarId}/subscriptions", request);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SubscriptionDto>())!;
    }

    private async Task<int> TickAsync(HttpMessageHandler handler)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var options = sp.GetRequiredService<IOptions<SubscriptionOptions>>();
        var service = new SubscriptionRefreshService(
            sp.GetRequiredService<SubscriptionsDbContext>(),
            sp.GetRequiredService<IQuerySession>(),
            new SubscriptionFeedClient(new HttpClient(handler), options),
            options,
            sp.GetRequiredService<IOptions<ItemTimeZoneOptions>>(),
            TimeProvider.System,
            NullLogger<SubscriptionRefreshService>.Instance);
        return await service.RunTickAsync(CancellationToken.None);
    }

    private async Task MakeDueAsync() => await ExecAsync("update cal_subscriptions.subscription set next_refresh_at = now() - interval '1 hour'");

    private async Task ExecAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Dictionary<string, string>> EventRowVersionsAsync()
    {
        await using var conn = new NpgsqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select source_key, xmin::text from cal_subscriptions.subscribed_event", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new Dictionary<string, string>();
        while (await reader.ReadAsync())
        {
            rows[reader.GetString(0)] = reader.GetString(1);
        }

        return rows;
    }

    private sealed class StubFeeds : HttpMessageHandler
    {
        public ConcurrentDictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> Routes { get; } = new();

        public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

        public static HttpResponseMessage Ok(string body, string? etag = null)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/calendar") };
            if (etag is not null)
            {
                resp.Headers.ETag = new(etag);
            }

            return resp;
        }

        public void Serve(string url, string body, string? etag = null) => Routes[url] = req =>
            etag is not null && req.Headers.IfNoneMatch.Any(t => t.Tag == etag)
                ? new HttpResponseMessage(HttpStatusCode.NotModified)
                : Ok(body, etag);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Enqueue(request);
            return Task.FromResult(Routes.TryGetValue(request.RequestUri!.AbsoluteUri, out var respond)
                ? respond(request)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
