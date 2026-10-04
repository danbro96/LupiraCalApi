using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Calendars;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>A timed item written over REST/MCP without a start zone gets its calendar's zone (a fixed-UTC calendar zone
/// doesn't count), else the configured <c>Items:DefaultTimezone</c>; the end zone follows. All-day items stay zone-less.</summary>
public sealed class ItemTimeZoneDefaultTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 7, 0, 0, TimeSpan.Zero);

    private static CreateCalendarItemRequest Timed(Guid calId, string? zone = null, string? rule = null) => new()
    {
        CalendarId = calId,
        Title = "Choir",
        IsAllDay = false,
        StartsAt = Start,
        EndsAt = Start.AddHours(2),
        StartTimezone = zone,
        RecurrenceRule = rule,
    };

    private static async Task<CalendarItemDto> CreateAsync(HttpClient api, CreateCalendarItemRequest r)
    {
        var resp = await api.PostAsJsonAsync("/items", r);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    private async Task<CalendarItem> StoredAsync(Guid id)
    {
        await using var q = Store.QuerySession();
        return (await q.LoadAsync<CalendarItem>(id))!;
    }

    private static async Task<Guid> CreateCalendarWithoutZoneAsync(HttpClient api)
    {
        var resp = await api.PostAsJsonAsync("/calendars", new CreateCalendarRequest { Slug = "work", Type = "calendar" });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContainerDto>())!.Id;
    }

    [Fact]
    public async Task Timed_create_without_zone_in_a_calendar_created_without_one_gets_the_configured_default()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarWithoutZoneAsync(api);

        var dto = await CreateAsync(api, Timed(calId));

        Assert.Equal("Europe/Stockholm", dto.StartTimezone);
        Assert.Equal("Europe/Stockholm", (await StoredAsync(dto.Id)).EndTimezone);
    }

    [Fact]
    public async Task Timed_create_without_zone_gets_the_calendar_zone()
    {
        var api = Factory.ApiClient(Email);
        var cal = await api.PostAsJsonAsync("/calendars", new CreateCalendarRequest { Slug = "ny", Type = "calendar", DefaultTimezone = "America/New_York" });
        cal.EnsureSuccessStatusCode();
        var calId = (await cal.Content.ReadFromJsonAsync<ContainerDto>())!.Id;

        var dto = await CreateAsync(api, Timed(calId));

        Assert.Equal("America/New_York", dto.StartTimezone);
    }

    [Fact]
    public async Task Timed_create_without_zone_honours_a_utc_calendar()
    {
        var api = Factory.ApiClient(Email);
        var cal = await api.PostAsJsonAsync("/calendars", new CreateCalendarRequest { Slug = "utc", Type = "calendar", DefaultTimezone = "UTC" });
        cal.EnsureSuccessStatusCode();
        var calId = (await cal.Content.ReadFromJsonAsync<ContainerDto>())!.Id;

        var dto = await CreateAsync(api, Timed(calId));

        Assert.Equal("UTC", dto.StartTimezone);
    }

    [Fact]
    public async Task Timed_create_keeps_a_supplied_zone_and_the_end_follows_it()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarWithoutZoneAsync(api);

        var dto = await CreateAsync(api, Timed(calId, "Europe/London"));

        Assert.Equal("Europe/London", dto.StartTimezone);
        Assert.Equal("Europe/London", (await StoredAsync(dto.Id)).EndTimezone);
    }

    [Fact]
    public async Task All_day_create_stays_zone_less()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarWithoutZoneAsync(api);

        var dto = await CreateAsync(api, new CreateCalendarItemRequest
        {
            CalendarId = calId, Title = "Holiday", IsAllDay = true, StartDate = new DateOnly(2026, 10, 19), EndDate = new DateOnly(2026, 10, 19),
        });

        Assert.Null(dto.StartTimezone);
    }

    [Fact]
    public async Task Weekly_item_without_zone_keeps_its_wall_clock_across_dst()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarWithoutZoneAsync(api);
        await CreateAsync(api, Timed(calId, rule: "FREQ=WEEKLY;COUNT=3"));

        var occ = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>(
            $"/items?calendarId={calId}&from=2026-10-18T00:00:00Z&to=2026-11-03T00:00:00Z");

        // 09:00 Stockholm: CEST before 25 Oct, CET after.
        Assert.Equal(
            [Start, new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero)],
            occ!.Select(o => o.Start));
    }

    [Fact]
    public async Task Update_from_all_day_to_timed_without_zone_gets_the_default()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarWithoutZoneAsync(api);
        var item = await CreateAsync(api, new CreateCalendarItemRequest
        {
            CalendarId = calId, Title = "Holiday", IsAllDay = true, StartDate = new DateOnly(2026, 10, 19), EndDate = new DateOnly(2026, 10, 19),
        });

        var resp = await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest
        {
            IsAllDay = false, StartsAt = Start, StartsAtProvided = true, EndsAt = Start.AddHours(1), EndsAtProvided = true,
        });

        resp.EnsureSuccessStatusCode();
        Assert.Equal("Europe/Stockholm", (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!.StartTimezone);
        Assert.Equal("Europe/Stockholm", (await StoredAsync(item.Id)).EndTimezone);
    }

    [Fact]
    public async Task Update_giving_a_zone_less_timed_item_a_rule_gets_the_default()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarWithoutZoneAsync(api);
        var item = await CreateAsync(api, Timed(calId, "UTC"));
        (await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest
        {
            StartTimezoneProvided = true, EndTimezoneProvided = true,
        })).EnsureSuccessStatusCode();
        Assert.Null((await StoredAsync(item.Id)).StartTimezone);

        var resp = await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest { RecurrenceRule = "FREQ=WEEKLY" });

        resp.EnsureSuccessStatusCode();
        Assert.Equal("Europe/Stockholm", (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!.StartTimezone);
    }

    [Fact]
    public async Task Configured_default_zone_applies()
    {
        var api = Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Items:DefaultTimezone"] = "Europe/Helsinki" }))).CreateClient();
        api.DefaultRequestHeaders.Add("X-Dev-User", Email);
        var calId = await CreateCalendarWithoutZoneAsync(api);

        var dto = await CreateAsync(api, Timed(calId));

        Assert.Equal("Europe/Helsinki", dto.StartTimezone);
    }
}
