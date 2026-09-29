using System.Net;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>A DAV PUT's exceptions/overrides persist as the item's structured deviations, expand in its zone, and render
/// back on GET.</summary>
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
        Assert.Contains("EXDATE:20261115T170000Z", body);
        Assert.Contains("RECURRENCE-ID:20261101T170000Z", body);
    }
}
