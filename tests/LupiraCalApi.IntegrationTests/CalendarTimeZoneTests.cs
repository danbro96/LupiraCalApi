using System.Net;
using System.Net.Http.Json;
using System.Text;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Calendars;
using LupiraCalApi.Core.Dtos.Me;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Every calendar carries an IANA zone: bootstrap and create use the supplied one, else the configured
/// <c>Items:DefaultTimezone</c>; an owner can change it later.</summary>
public sealed class CalendarTimeZoneTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";

    private static async Task<List<ContainerDto>> BootstrapAsync(HttpClient api, BootstrapRequest? body = null)
    {
        var resp = body is null ? await api.PostAsync("/me/bootstrap", null) : await api.PostAsJsonAsync("/me/bootstrap", body);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<ContainerDto>>())!;
    }

    private static async Task<ContainerDto> CreateAsync(HttpClient api, string? zone)
    {
        var resp = await api.PostAsJsonAsync("/calendars", new CreateCalendarRequest { Slug = "work", Type = "calendar", DefaultTimezone = zone });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContainerDto>())!;
    }

    [Fact]
    public async Task Bootstrap_without_a_body_gives_the_configured_default()
    {
        var seeded = await BootstrapAsync(Factory.ApiClient(Email));

        Assert.Equal(8, seeded.Count);
        Assert.All(seeded, c => Assert.Equal("Europe/Stockholm", c.DefaultTimezone));
    }

    [Fact]
    public async Task Bootstrap_gives_new_calendars_the_supplied_zone()
    {
        var seeded = await BootstrapAsync(Factory.ApiClient(Email), new BootstrapRequest { DefaultTimezone = "America/New_York" });

        Assert.All(seeded, c => Assert.Equal("America/New_York", c.DefaultTimezone));
    }

    [Fact]
    public async Task Bootstrap_leaves_existing_calendars_zones_alone()
    {
        var api = Factory.ApiClient(Email);
        await BootstrapAsync(api);

        var again = await BootstrapAsync(api, new BootstrapRequest { DefaultTimezone = "America/New_York" });

        Assert.All(again, c => Assert.Equal("Europe/Stockholm", c.DefaultTimezone));
    }

    [Fact]
    public async Task Bootstrap_accepts_an_empty_json_body()
    {
        var resp = await Factory.ApiClient(Email).PostAsync("/me/bootstrap", new StringContent(string.Empty, Encoding.UTF8, "application/json"));

        resp.EnsureSuccessStatusCode();
        Assert.All((await resp.Content.ReadFromJsonAsync<List<ContainerDto>>())!, c => Assert.Equal("Europe/Stockholm", c.DefaultTimezone));
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("W. Europe Standard Time")]
    [InlineData("")]
    public async Task Bootstrap_rejects_a_non_iana_zone_and_creates_nothing(string zone)
    {
        var api = Factory.ApiClient(Email);

        var resp = await api.PostAsJsonAsync("/me/bootstrap", new BootstrapRequest { DefaultTimezone = zone });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty((await api.GetFromJsonAsync<List<ContainerDto>>("/calendars"))!);
    }

    [Fact]
    public async Task Bootstrap_uses_a_configured_default()
    {
        var api = Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Items:DefaultTimezone"] = "Europe/Helsinki" }))).CreateClient();
        api.DefaultRequestHeaders.Add("X-Dev-User", Email);

        Assert.All(await BootstrapAsync(api), c => Assert.Equal("Europe/Helsinki", c.DefaultTimezone));
    }

    [Fact]
    public async Task Create_without_a_zone_gets_the_configured_default()
    {
        Assert.Equal("Europe/Stockholm", (await CreateAsync(Factory.ApiClient(Email), null)).DefaultTimezone);
    }

    [Fact]
    public async Task Create_keeps_an_explicit_utc()
    {
        Assert.Equal("UTC", (await CreateAsync(Factory.ApiClient(Email), "UTC")).DefaultTimezone);
    }

    [Fact]
    public async Task Create_rejects_a_non_iana_zone()
    {
        var resp = await Factory.ApiClient(Email).PostAsJsonAsync("/calendars",
            new CreateCalendarRequest { Slug = "work", Type = "calendar", DefaultTimezone = "Mars/Olympus" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_changes_the_zone_new_timed_items_get()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateAsync(api, null);

        var resp = await api.PutAsJsonAsync($"/calendars/{cal.Id}", new UpdateCalendarRequest { DefaultTimezone = "America/New_York" });

        resp.EnsureSuccessStatusCode();
        Assert.Equal("America/New_York", (await resp.Content.ReadFromJsonAsync<ContainerDto>())!.DefaultTimezone);
        Assert.Equal("America/New_York", (await api.GetFromJsonAsync<List<ContainerDto>>("/calendars"))!.Single().DefaultTimezone);
        var start = new DateTimeOffset(2026, 10, 19, 13, 0, 0, TimeSpan.Zero);
        var item = await api.PostAsJsonAsync("/items",
            new CreateCalendarItemRequest { CalendarId = cal.Id, Title = "Call", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1) });
        Assert.Equal("America/New_York", (await item.Content.ReadFromJsonAsync<CalendarItemDto>())!.StartTimezone);
    }

    [Fact]
    public async Task Update_rejects_a_non_iana_zone()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateAsync(api, null);

        var resp = await api.PutAsJsonAsync($"/calendars/{cal.Id}", new UpdateCalendarRequest { DefaultTimezone = "Mars/Olympus" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_is_owner_only()
    {
        var alice = Factory.ApiClient(Email);
        var cal = await CreateAsync(alice, null);
        (await alice.PostAsJsonAsync($"/calendars/{cal.Id}/owners", new GrantOwnerRequest { Email = "bob@x.test", Access = "read-write" })).EnsureSuccessStatusCode();

        var resp = await Factory.ApiClient("bob@x.test").PutAsJsonAsync($"/calendars/{cal.Id}", new UpdateCalendarRequest { DefaultTimezone = "America/New_York" });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Update_of_an_unknown_calendar_is_not_found()
    {
        var resp = await Factory.ApiClient(Email).PutAsJsonAsync($"/calendars/{Guid.NewGuid()}", new UpdateCalendarRequest { DefaultTimezone = "America/New_York" });

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Mcp_bootstrap_and_update_set_the_zone()
    {
        var http = Factory.ApiClient(Email);
        await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, ownsHttpClient: true));

        var boot = await mcp.CallToolAsync("bootstrap_me", new Dictionary<string, object?> { ["defaultTimezone"] = "Europe/London" });
        Assert.NotEqual(true, boot.IsError);
        var cals = await Factory.ApiClient(Email).GetFromJsonAsync<List<ContainerDto>>("/calendars");
        Assert.All(cals!, c => Assert.Equal("Europe/London", c.DefaultTimezone));

        var personal = cals!.Single(c => c.Slug == "personal").Id;
        var update = await mcp.CallToolAsync("update_calendar", new Dictionary<string, object?>
        {
            ["calendarId"] = personal,
            ["request"] = new Dictionary<string, object?> { ["defaultTimezone"] = "Europe/Stockholm" },
        });
        Assert.NotEqual(true, update.IsError);
        var after = await Factory.ApiClient(Email).GetFromJsonAsync<List<ContainerDto>>("/calendars");
        Assert.Equal("Europe/Stockholm", after!.Single(c => c.Id == personal).DefaultTimezone);
    }
}
