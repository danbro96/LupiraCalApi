using System.Net;
using System.Net.Http.Json;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>A recurring item's per-occurrence deviations: from a DAV PUT or the REST occurrence routes they persist
/// structured, expand in the item's zone, and render back to DAV clients in that zone.</summary>
public sealed class OccurrenceDeviationTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";
    private static readonly DateTimeOffset Start = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Dav_put_exceptions_and_overrides_persist_structured_and_render_back()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        const string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//lupira-test//EN\r\n" +
            "BEGIN:VEVENT\r\nUID:series@x\r\nDTSTART;TZID=Europe/Stockholm:20260517T180000\r\nDTEND;TZID=Europe/Stockholm:20260517T210000\r\n" +
            "SUMMARY:Middag\r\nRRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=SU\r\nEXDATE;TZID=Europe/Stockholm:20261115T180000\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:series@x\r\nRECURRENCE-ID;TZID=Europe/Stockholm:20261101T180000\r\n" +
            "DTSTART;TZID=Europe/Stockholm:20261101T190000\r\nDTEND;TZID=Europe/Stockholm:20261101T220000\r\nSUMMARY:Middag\r\nEND:VEVENT\r\n" +
            "END:VCALENDAR\r\n";

        Assert.Equal(HttpStatusCode.Created, (await PutIcsBackendAsync(api, Email, cal, "series@x", ics)).StatusCode);

        await using var query = Store.QuerySession();
        var item = await query.LoadAsync<CalendarItem>(DeterministicGuid.From("series@x"));
        Assert.Equal("Europe/Stockholm", item!.StartTimezone);
        // Winter: 18:00 CET = 17:00Z; the moved 11-01 occurrence is 19:00 CET = 18:00Z; 11-15 is excluded.
        var occ = new RecurrenceExpander().Expand(item, new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal([new DateTimeOffset(2026, 11, 1, 18, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 29, 17, 0, 0, TimeSpan.Zero)], occ);

        var body = await (await GetIcsBackendAsync(api, Email, cal, "series@x")).Content.ReadAsStringAsync();
        Assert.Contains("DTSTART;TZID=Europe/Stockholm:20260517T180000", body);
        Assert.Contains("EXDATE;TZID=Europe/Stockholm:20261115T180000", body);
        Assert.Contains("RECURRENCE-ID;TZID=Europe/Stockholm:20261101T180000", body);
    }

    private static async Task<CalendarItemDto> CreateWeeklyAsync(HttpClient api, Guid cal)
    {
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = cal, Title = "Middag", IsAllDay = false, StartsAt = new DateTimeOffset(2026, 10, 4, 16, 0, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 10, 4, 19, 0, 0, TimeSpan.Zero), StartTimezone = "Europe/Stockholm", RecurrenceRule = "FREQ=WEEKLY;BYDAY=SU",
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    private static async Task<List<CalendarItemOccurrenceDto>> OccurrencesAsync(HttpClient api, Guid itemId) =>
        (await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>("/items/?from=2026-10-01T00:00:00Z&to=2026-10-26T00:00:00Z"))!
            .Where(o => o.Id == itemId).ToList();

    [Fact]
    public async Task Rest_moves_excludes_and_restores_single_occurrences()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var item = await CreateWeeklyAsync(api, cal);
        var oct11 = new DateTimeOffset(2026, 10, 11, 16, 0, 0, TimeSpan.Zero);
        var oct18 = new DateTimeOffset(2026, 10, 18, 16, 0, 0, TimeSpan.Zero);

        var move = await api.PutAsJsonAsync($"/items/{item.Id}/occurrences/{oct11:yyyy-MM-ddTHH:mm:ssZ}",
            new ChangeOccurrenceRequest { StartsAt = oct11.AddDays(1), EndsAt = oct11.AddDays(1).AddHours(2), Title = "Middag (måndag)" });
        move.EnsureSuccessStatusCode();
        var dto = (await move.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal(oct11, Assert.Single(dto.OccurrenceOverrides!).OriginalStart);
        (await api.PutAsJsonAsync($"/items/{item.Id}/occurrences/{oct18:yyyy-MM-ddTHH:mm:ssZ}", new ChangeOccurrenceRequest { Excluded = true })).EnsureSuccessStatusCode();

        var occ = await OccurrencesAsync(api, item.Id);
        Assert.Equal([new DateTimeOffset(2026, 10, 4, 16, 0, 0, TimeSpan.Zero), oct11.AddDays(1), new DateTimeOffset(2026, 10, 25, 17, 0, 0, TimeSpan.Zero)],
            occ.Select(o => o.Start).ToList());
        var monday = occ.Single(o => o.Start == oct11.AddDays(1));
        Assert.Equal("Middag (måndag)", monday.Title);
        Assert.Equal(oct11.AddDays(1).AddHours(2), monday.End);

        var body = await (await GetIcsBackendAsync(api, Email, cal, item.ExternalId)).Content.ReadAsStringAsync();
        Assert.Contains("RECURRENCE-ID", body);
        Assert.Contains("EXDATE", body);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/items/{item.Id}/occurrences/{oct18:yyyy-MM-ddTHH:mm:ssZ}")).StatusCode);
        Assert.Contains(oct18, (await OccurrencesAsync(api, item.Id)).Select(o => o.Start));
    }

    [Fact]
    public async Task Occurrence_changes_are_rejected_off_the_series()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var item = await CreateWeeklyAsync(api, cal);

        var offSeries = await api.PutAsJsonAsync($"/items/{item.Id}/occurrences/2026-10-12T16:00:00Z", new ChangeOccurrenceRequest { Excluded = true });
        Assert.Equal(HttpStatusCode.BadRequest, offSeries.StatusCode);
        var empty = await api.PutAsJsonAsync($"/items/{item.Id}/occurrences/2026-10-11T16:00:00Z", new ChangeOccurrenceRequest());
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }
}
