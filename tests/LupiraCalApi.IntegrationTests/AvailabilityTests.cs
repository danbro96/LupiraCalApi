using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class AvailabilityTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static async Task<CalendarItemDto> PostAsync(HttpClient api, CreateCalendarItemRequest req)
    {
        var resp = await api.PostAsJsonAsync("/items", req);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    [Fact]
    public async Task A_day_can_hold_a_whole_day_and_a_timed_segment()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var day = new DateOnly(2026, 7, 1);

        var office = await PostAsync(api, new CreateCalendarItemRequest
        {
            CalendarId = calId, Title = "Office", Availability = AvailabilityStatus.Office,
            IsAllDay = true, StartDate = day, EndDate = day.AddDays(1),
        });
        var home = await PostAsync(api, new CreateCalendarItemRequest
        {
            CalendarId = calId, Title = "Home", Availability = AvailabilityStatus.Home,
            IsAllDay = false, StartsAt = new DateTimeOffset(2026, 7, 1, 13, 0, 0, TimeSpan.Zero), EndsAt = new DateTimeOffset(2026, 7, 1, 17, 0, 0, TimeSpan.Zero), StartTimezone = "UTC",
        });

        Assert.Equal(AvailabilityStatus.Office, office.Details?.Presence?.Status);
        Assert.Equal(AvailabilityStatus.Home, home.Details?.Presence?.Status);

        var from = Uri.EscapeDataString(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero).ToString("o"));
        var to = Uri.EscapeDataString(new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero).ToString("o"));
        var occ = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/items?calendarId={calId}&from={from}&to={to}");
        Assert.Equal(2, occ!.Count(o => o.Id == office.Id || o.Id == home.Id));   // both segments coexist on the day
    }

    [Fact]
    public async Task Availability_status_can_be_updated()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await PostAsync(api, new CreateCalendarItemRequest
        {
            CalendarId = calId, Title = "Status", Availability = AvailabilityStatus.Office,
            IsAllDay = true, StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 7, 2),
        });

        var upd = await api.PutAsJsonAsync($"/items/{item.Id}", new UpdateCalendarItemRequest { Availability = AvailabilityStatus.Sick });
        upd.EnsureSuccessStatusCode();

        var got = await api.GetFromJsonAsync<CalendarItemDto>($"/items/{item.Id}");
        Assert.Equal(AvailabilityStatus.Sick, got!.Details?.Presence?.Status);
    }

    [Fact]
    public async Task A_recurring_default_week_expands_to_each_occurrence()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var start = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);   // a Wednesday
        await PostAsync(api, new CreateCalendarItemRequest
        {
            CalendarId = calId, Title = "Office hours", Availability = AvailabilityStatus.Office,
            IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(8), StartTimezone = "UTC", RecurrenceRule = "FREQ=WEEKLY",
        });

        var from = Uri.EscapeDataString(start.ToString("o"));
        var to = Uri.EscapeDataString(start.AddDays(21).ToString("o"));
        var occ = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/items?calendarId={calId}&from={from}&to={to}");
        Assert.True(occ!.Count >= 3, $"expected >=3 weekly occurrences, got {occ!.Count}");
    }
}
