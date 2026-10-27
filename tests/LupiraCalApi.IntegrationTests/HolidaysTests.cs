using System.Net;
using System.Net.Http.Json;
using Lupira.Contracts.Fires;
using Lupira.Testing.Postgres;
using LupiraCalApi.Core.Domain.Holidays;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Calendars;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>The Holidays calendar is not stored — cal-api computes Swedish public holidays and eves at read time.</summary>
public sealed class HolidaysTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "holidays@x.test";

    private static readonly DateTimeOffset Year2026 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<List<ContainerDto>> BootstrapAsync(HttpClient api)
    {
        (await api.PostAsync("/me/bootstrap", null)).EnsureSuccessStatusCode();
        return (await api.GetFromJsonAsync<List<ContainerDto>>("/calendars"))!;
    }

    private static async Task<List<CalendarItemOccurrenceDto>> SearchAsync(HttpClient api, string query)
    {
        var url = $"/items?from={Uri.EscapeDataString(Year2026.ToString("o"))}&to={Uri.EscapeDataString(Year2026.AddYears(1).ToString("o"))}{query}";
        return (await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>(url))!;
    }

    [Fact]
    public async Task Bootstrap_adds_the_helgdagar_agenda_calendar()
    {
        var cals = await BootstrapAsync(Factory.ApiClient(Email));

        var holidays = Assert.Single(cals, c => c.Kind == CalendarKind.Holidays);
        Assert.Equal("Helgdagar", holidays.DisplayName);
        Assert.Equal(CalendarClass.Agenda, holidays.Class);
    }

    [Fact]
    public async Task A_year_has_twenty_computed_all_day_holidays()
    {
        var api = Factory.ApiClient(Email);
        var holidays = (await BootstrapAsync(api)).Single(c => c.Kind == CalendarKind.Holidays).Id;

        var occ = await SearchAsync(api, $"&calendarId={holidays}");

        Assert.Equal(20, occ.Count);
        Assert.Equal(13, occ.Count(o => o.Holiday == HolidayKind.PublicHoliday));
        Assert.All(occ, o =>
        {
            Assert.True(o.IsAllDay);
            Assert.Equal(OriginKind.Holiday, o.Origin?.Kind);
            Assert.Null(o.Completeness);
            Assert.Equal([holidays], o.CalendarIds);
        });
        var midsommar = occ.Single(o => o.Title == "Midsommardagen");
        Assert.Equal(new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero), midsommar.Start);
    }

    [Fact]
    public async Task Filters_that_cannot_match_a_holiday_exclude_them()
    {
        var api = Factory.ApiClient(Email);
        var cals = await BootstrapAsync(api);
        var personal = cals.Single(c => c.Kind == CalendarKind.Personal).Id;

        Assert.Empty(await SearchAsync(api, $"&calendarId={personal}"));
        Assert.DoesNotContain(await SearchAsync(api, "&status=Cancelled"), o => o.Holiday is not null);
        Assert.Equal(["Midsommarafton", "Midsommardagen"], (await SearchAsync(api, "&query=midsommar")).Select(o => o.Title));
    }

    [Fact]
    public async Task Stored_items_cannot_be_created_in_or_filed_into_the_holidays_calendar()
    {
        var api = Factory.ApiClient(Email);
        var holidays = (await BootstrapAsync(api)).Single(c => c.Kind == CalendarKind.Holidays).Id;
        var start = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

        var create = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = holidays,
            Title = "Shadow",
            IsAllDay = false,
            StartsAt = start,
            EndsAt = start.AddHours(1),
            StartTimezone = "UTC",
        });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);

        var elsewhere = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            Title = "Loose",
            IsAllDay = false,
            StartsAt = start,
            EndsAt = start.AddHours(1),
            StartTimezone = "UTC",
        });
        elsewhere.EnsureSuccessStatusCode();
        var item = (await elsewhere.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        var file = await api.PostAsync($"/items/{item.Id}/calendars/{holidays}?status=accepted", null);
        Assert.Equal(HttpStatusCode.BadRequest, file.StatusCode);
    }
}
