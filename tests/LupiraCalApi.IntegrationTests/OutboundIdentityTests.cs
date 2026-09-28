using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Hotspots;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>The real geo/contact/photo clients over a stubbed network: calls with no member behind them (the DAV seam) use
/// the service client's credentials; member calls carry the member's identity. Development stands in for the
/// member-token exchange, which the unit tests cover.</summary>
public sealed class OutboundIdentityTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";
    private const string DavGateway = "lupira-dav-svc";
    private const string TokenUrl = "http://auth.test/application/o/token/";

    private sealed class Upstreams : HttpMessageHandler
    {
        public List<(string Path, string? GrantType, string? ClientId, string? Authorization, string? DevUser)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var auth = request.Headers.Authorization?.ToString();
            var dev = request.Headers.TryGetValues("X-Dev-User", out var d) ? d.Single() : null;

            if (request.RequestUri.Host == "auth.test")
            {
                var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
                lock (Calls) Calls.Add((path, form["grant_type"], form["client_id"], auth, dev));
                return Json($$"""{"access_token":"cc-{{form["client_id"]}}","expires_in":300}""");
            }

            lock (Calls) Calls.Add((path, null, null, auth, dev));
            if (path == "/places/resolve")
                return Json($$"""{"placeId":"{{Guid.NewGuid()}}","name":"Cafe Central","latitude":48.2,"longitude":16.4}""");
            if (path == "/places/lookup")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var ids = doc.RootElement.GetProperty("ids").EnumerateArray().Select(e => e.GetGuid());
                return Json(JsonSerializer.Serialize(ids.Select(id => new { requestedId = id, place = new { id, name = "Cafe Central", latitude = 59.3301, longitude = 18.0701 } })));
            }
            if (path == "/places")
                return Json("[]");
            if (path == "/photos/density")
                return Json("""[{"latitude":59.33,"longitude":18.07,"count":4,"days":["2026-03-01","2026-03-02"]}]""");
            if (path == "/contacts/lookup")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var ids = doc.RootElement.GetProperty("contactIds").EnumerateArray().Select(e => e.GetGuid());
                return Json(JsonSerializer.Serialize(ids.Select(id => new { contactId = id, displayName = "Jane Doe" })));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private (HttpClient Member, HttpClient Dav, Upstreams Net) Clients()
    {
        var net = new Upstreams();
        var app = Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("DavGateway:ClientId", DavGateway);
            b.UseSetting("Geo:BaseUrl", "http://geo.test/");
            b.UseSetting("Geo:Audience", "lupira-geo");
            b.UseSetting("Geo:TokenUrl", TokenUrl);
            b.UseSetting("Geo:ClientId", "lupira-geo-svc");
            b.UseSetting("Geo:ClientSecret", "geo-secret");
            b.UseSetting("Contacts:BaseUrl", "http://contact.test/");
            b.UseSetting("Contacts:Audience", "lupira-contact");
            b.UseSetting("Contacts:TokenUrl", TokenUrl);
            b.UseSetting("Contacts:ClientId", "lupira-contact-svc");
            b.UseSetting("Contacts:ClientSecret", "contact-secret");
            b.UseSetting("Photos:BaseUrl", "http://photo.test/");
            b.UseSetting("Photos:Audience", "lupira-photo");
            b.ConfigureTestServices(s => s.ConfigureHttpClientDefaults(h => h.ConfigurePrimaryHttpMessageHandler(() => net)));
        });
        var member = app.CreateClient();
        member.DefaultRequestHeaders.Add("X-Dev-User", Email);
        var dav = app.CreateClient();
        dav.DefaultRequestHeaders.Add("X-Dev-User", Email);
        dav.DefaultRequestHeaders.Add("X-Dev-Client", DavGateway);
        return (member, dav, net);
    }

    private static string IcsWithLocation(string uid) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//lupira-test//EN\r\nBEGIN:VEVENT\r\n" +
        $"UID:{uid}\r\nSUMMARY:Coffee\r\nDTSTART:20260701T090000Z\r\nDTEND:20260701T100000Z\r\n" +
        "LOCATION:cafe central\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    [Fact]
    public async Task Dav_put_resolves_places_with_the_service_credential()
    {
        var (member, dav, net) = Clients();
        var calId = await CreateCalendarAsync(member);

        (await PutIcsBackendAsync(dav, Email, calId, $"{Guid.NewGuid():N}@test", IcsWithLocation("a"))).EnsureSuccessStatusCode();

        Assert.Contains(net.Calls, c => c.GrantType == "client_credentials" && c.ClientId == "lupira-geo-svc");
        var geo = Assert.Single(net.Calls, c => c.Path == "/places/resolve");
        Assert.Equal("Bearer cc-lupira-geo-svc", geo.Authorization);
        Assert.Null(geo.DevUser);
    }

    [Fact]
    public async Task Dav_puts_reuse_the_cached_service_token()
    {
        var (member, dav, net) = Clients();
        var calId = await CreateCalendarAsync(member);

        (await PutIcsBackendAsync(dav, Email, calId, $"{Guid.NewGuid():N}@test", IcsWithLocation("a"))).EnsureSuccessStatusCode();
        (await PutIcsBackendAsync(dav, Email, calId, $"{Guid.NewGuid():N}@test", IcsWithLocation("b"))).EnsureSuccessStatusCode();

        Assert.Single(net.Calls, c => c.GrantType is not null);
        Assert.Equal(2, net.Calls.Count(c => c.Path == "/places/resolve"));
    }

    [Fact]
    public async Task Member_participant_check_carries_the_members_identity()
    {
        var (member, _, net) = Clients();
        var calId = await CreateCalendarAsync(member);
        var item = (await (await member.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = calId,
            Title = "Party",
            Category = "General",
            IsAllDay = false,
            StartsAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            StartTimezone = "UTC",
        })).EnsureSuccessStatusCode().Content.ReadFromJsonAsync<CalendarItemDto>())!;

        (await member.PutAsJsonAsync($"/items/{item.Id}/participants",
            new SetParticipantsRequest { ContactIds = [Guid.NewGuid()] })).EnsureSuccessStatusCode();

        var lookup = Assert.Single(net.Calls, c => c.Path == "/contacts/lookup");
        Assert.Equal(Email, lookup.DevUser);
        Assert.Null(lookup.Authorization);
        Assert.DoesNotContain(net.Calls, c => c.GrantType is not null);
    }

    [Fact]
    public async Task Member_hotspots_read_geo_and_photos_as_the_member()
    {
        var (member, _, net) = Clients();
        var calId = await CreateCalendarAsync(member);
        (await member.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = calId,
            Title = "Coffee",
            IsAllDay = false,
            StartsAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            StartTimezone = "UTC",
            Location = "Cafe Central",
            PlaceId = Guid.NewGuid(),
        })).EnsureSuccessStatusCode();

        var hotspot = Assert.Single((await member.GetFromJsonAsync<List<HotspotDto>>("/hotspots?to=2026-12-31T00:00:00Z"))!);

        Assert.Equal((1, 4, 3), (hotspot.EventCount, hotspot.PhotoCount, hotspot.ActiveDays));
        Assert.Equal("Cafe Central", hotspot.Label);
        Assert.Equal(Email, Assert.Single(net.Calls, c => c.Path == "/photos/density").DevUser);
        Assert.Equal(Email, Assert.Single(net.Calls, c => c.Path == "/places/lookup").DevUser);
        Assert.DoesNotContain(net.Calls, c => c.GrantType is not null);
    }
}
