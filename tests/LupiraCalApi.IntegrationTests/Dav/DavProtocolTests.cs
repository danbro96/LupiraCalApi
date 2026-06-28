using LupiraCalApi.Application;
using LupiraCalApi.Dav;
using LupiraCalApi.Domain;
using LupiraCalApi.Serialization;
using System.Xml.Linq;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Fast unit tests for the pure DAV protocol logic extracted from <c>DavRouter</c> — no HTTP, no Postgres,
/// no container fixture (deliberately NOT in the "integration" collection). These pin the fiddly edge cases the
/// integration tests don't exhaust: overlap boundaries, malformed tokens/time-ranges, and hostile request bodies.</summary>
public class DavProtocolTests
{
    static readonly DateTimeOffset Nine = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    static DateTimeOffset Utc(int y, int m, int d, int h = 0, int min = 0) => new(y, m, d, h, min, 0, TimeSpan.Zero);

    // ---------- OverlapsWindow (half-open [start, end)) ----------

    [Fact]
    public void Overlap_timed_event_inside_window()
    {
        var i = new CalendarItem { StartsAt = Nine, EndsAt = Nine.AddHours(1) };
        Assert.True(DavProtocol.OverlapsWindow(i, Utc(2026, 7, 1, 8), Utc(2026, 7, 1, 11), Expander()));
    }

    [Fact]
    public void Overlap_timed_event_entirely_before_or_after_is_false()
    {
        var before = new CalendarItem { StartsAt = Utc(2026, 7, 1, 6), EndsAt = Utc(2026, 7, 1, 7) };
        var after = new CalendarItem { StartsAt = Utc(2026, 7, 1, 12), EndsAt = Utc(2026, 7, 1, 13) };
        Assert.False(DavProtocol.OverlapsWindow(before, Utc(2026, 7, 1, 8), Utc(2026, 7, 1, 11), Expander()));
        Assert.False(DavProtocol.OverlapsWindow(after, Utc(2026, 7, 1, 8), Utc(2026, 7, 1, 11), Expander()));
    }

    [Fact]
    public void Overlap_left_edge_is_inclusive_right_edge_is_exclusive()
    {
        // Event ending exactly at window start overlaps (en >= start).
        var touchesStart = new CalendarItem { StartsAt = Utc(2026, 7, 1, 7), EndsAt = Utc(2026, 7, 1, 8) };
        Assert.True(DavProtocol.OverlapsWindow(touchesStart, Utc(2026, 7, 1, 8), Utc(2026, 7, 1, 10), Expander()));

        // Event starting exactly at window end does not overlap (s < end is strict).
        var touchesEnd = new CalendarItem { StartsAt = Utc(2026, 7, 1, 10), EndsAt = Utc(2026, 7, 1, 11) };
        Assert.False(DavProtocol.OverlapsWindow(touchesEnd, Utc(2026, 7, 1, 8), Utc(2026, 7, 1, 10), Expander()));
    }

    [Fact]
    public void Overlap_all_day_item_maps_to_midnight_utc()
    {
        var allDay = new CalendarItem { IsAllDay = true, StartDate = new DateOnly(2026, 7, 1) };
        Assert.True(DavProtocol.OverlapsWindow(allDay, Utc(2026, 7, 1), Utc(2026, 7, 2), Expander()));
        Assert.False(DavProtocol.OverlapsWindow(allDay, Utc(2026, 7, 2), Utc(2026, 7, 3), Expander()));
    }

    [Fact]
    public void Overlap_all_day_span_uses_end_date()
    {
        var span = new CalendarItem { IsAllDay = true, StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 7, 2) };
        // An afternoon window on the start day overlaps because the span runs to the next midnight.
        Assert.True(DavProtocol.OverlapsWindow(span, Utc(2026, 7, 1, 12), Utc(2026, 7, 1, 13), Expander()));
    }

    [Fact]
    public void Overlap_event_with_no_end_falls_back_to_a_zero_duration_instant()
    {
        var instant = new CalendarItem { StartsAt = Nine };   // EndsAt null, not all-day
        Assert.True(DavProtocol.OverlapsWindow(instant, Utc(2026, 7, 1, 8), Utc(2026, 7, 1, 10), Expander()));
        Assert.False(DavProtocol.OverlapsWindow(instant, Utc(2026, 7, 1, 10), Utc(2026, 7, 1, 11), Expander()));
    }

    [Fact]
    public void Overlap_recurring_item_delegates_to_the_expander()
    {
        var i = new CalendarItem { StartsAt = Nine, EndsAt = Nine.AddHours(1), RecurrenceRule = "FREQ=WEEKLY" };   // expander regenerates ICS from these

        Assert.True(DavProtocol.OverlapsWindow(i, Utc(2026, 7, 15), Utc(2026, 7, 16), Expander()));   // an occurrence lands here
        Assert.False(DavProtocol.OverlapsWindow(i, Utc(2026, 7, 16), Utc(2026, 7, 17), Expander()));  // none here
    }

    [Fact]
    public void Overlap_item_with_no_start_is_false()
    {
        Assert.False(DavProtocol.OverlapsWindow(new CalendarItem(), Utc(2026, 7, 1), Utc(2026, 7, 2), Expander()));
    }

    static RecurrenceExpander Expander() => new();

    // ---------- ParseICalUtc ----------

    [Fact]
    public void ParseICalUtc_reads_a_valid_utc_stamp() =>
        Assert.Equal(Nine, DavProtocol.ParseICalUtc("20260701T090000Z"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("20260701")]               // date only
    [InlineData("2026-07-01T09:00:00Z")]   // ISO with separators — not the iCal basic format
    [InlineData("20260701T090000")]        // missing Z
    public void ParseICalUtc_rejects_anything_that_is_not_basic_utc(string? s) =>
        Assert.Null(DavProtocol.ParseICalUtc(s));

    // ---------- ParseTimeRange ----------

    [Fact]
    public void ParseTimeRange_extracts_start_and_end()
    {
        var doc = XDocument.Parse("""<c:q xmlns:c="urn:ietf:params:xml:ns:caldav"><c:time-range start="20260701T000000Z" end="20260702T000000Z"/></c:q>""");
        var r = DavProtocol.ParseTimeRange(doc);
        Assert.NotNull(r);
        Assert.Equal(Utc(2026, 7, 1), r!.Value.Start);
        Assert.Equal(Utc(2026, 7, 2), r.Value.End);
    }

    [Fact]
    public void ParseTimeRange_returns_null_when_an_attribute_is_missing()
    {
        var doc = XDocument.Parse("""<c:q xmlns:c="urn:ietf:params:xml:ns:caldav"><c:time-range start="20260701T000000Z"/></c:q>""");
        Assert.Null(DavProtocol.ParseTimeRange(doc));
    }

    [Fact]
    public void ParseTimeRange_returns_null_without_an_element_or_doc()
    {
        Assert.Null(DavProtocol.ParseTimeRange(XDocument.Parse("<c:q xmlns:c=\"urn:ietf:params:xml:ns:caldav\"/>")));
        Assert.Null(DavProtocol.ParseTimeRange(null));
    }

    // ---------- ParseSyncToken ----------

    [Fact]
    public void ParseSyncToken_reads_a_numeric_token() =>
        Assert.Equal(42L, DavProtocol.ParseSyncToken(XDocument.Parse("""<d:s xmlns:d="DAV:"><d:sync-token>42</d:sync-token></d:s>""")));

    [Theory]
    [InlineData("""<d:s xmlns:d="DAV:"><d:sync-token></d:sync-token></d:s>""")]   // empty
    [InlineData("""<d:s xmlns:d="DAV:"><d:sync-token>abc</d:sync-token></d:s>""")] // non-numeric
    [InlineData("""<d:s xmlns:d="DAV:"></d:s>""")]                                  // missing element
    public void ParseSyncToken_returns_null_for_empty_missing_or_garbage(string xml) =>
        Assert.Null(DavProtocol.ParseSyncToken(XDocument.Parse(xml)));

    // ---------- ExtractHrefUids ----------

    [Fact]
    public void ExtractHrefUids_strips_extension_and_path_and_dedups()
    {
        var body = """<d:m xmlns:d="DAV:"><d:href>/dav/u/1/cal/2/a@x.ics</d:href><d:href>/dav/u/1/cal/2/b@x.ics</d:href><d:href>/dav/u/1/cal/2/a@x.ics</d:href></d:m>""";
        var uids = DavProtocol.ExtractHrefUids(body, ".ics");
        Assert.Equal(2, uids.Count);
        Assert.Contains("a@x", uids);
        Assert.Contains("b@x", uids);
    }

    [Fact]
    public void ExtractHrefUids_handles_trailing_slash_and_case_insensitive_ext()
    {
        var body = """<d:m xmlns:d="DAV:"><d:href>/dav/c@x.ICS/</d:href></d:m>""";
        Assert.Equal(["c@x"], DavProtocol.ExtractHrefUids(body, ".ics"));
    }

    [Fact]
    public void ExtractHrefUids_only_strips_the_matching_extension()
    {
        var body = """<d:m xmlns:d="DAV:"><d:href>/dav/a@x.ics</d:href></d:m>""";
        Assert.Equal(["a@x"], DavProtocol.ExtractHrefUids(body, ".ics"));
        Assert.Equal(["a@x.ics"], DavProtocol.ExtractHrefUids(body, ".vcf"));   // non-matching ext left intact
    }

    [Theory]
    [InlineData("<not valid xml")]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractHrefUids_returns_empty_for_blank_or_malformed_bodies(string body) =>
        Assert.Empty(DavProtocol.ExtractHrefUids(body, ".ics"));

    // ---------- StripExt ----------

    [Theory]
    [InlineData("a@x.ics", "a@x")]
    [InlineData("noext", "noext")]
    [InlineData("a.b.ics", "a.b")]
    public void StripExt_removes_the_last_extension(string input, string expected) =>
        Assert.Equal(expected, DavProtocol.StripExt(input));

    // ---------- DavStatus ----------

    [Theory]
    [InlineData(OpStatus.Ok, 204)]
    [InlineData(OpStatus.Forbidden, 403)]
    [InlineData(OpStatus.NotFound, 404)]
    [InlineData(OpStatus.Conflict, 412)]
    [InlineData(OpStatus.Invalid, 400)]
    public void DavStatus_maps_op_status_to_http_code(OpStatus status, int expected) =>
        Assert.Equal(expected, DavProtocol.DavStatus(status));

    // ---------- TryParseXml ----------

    [Fact]
    public void TryParseXml_parses_valid_and_rejects_invalid()
    {
        Assert.NotNull(DavProtocol.TryParseXml("<a/>"));
        Assert.Null(DavProtocol.TryParseXml("<a"));
        Assert.Null(DavProtocol.TryParseXml("   "));
    }

    // ---------- ParsePreconditions ----------

    [Fact]
    public void ParsePreconditions_unquotes_a_concrete_if_match()
    {
        var (ifMatch, star) = DavProtocol.ParsePreconditions("  \"abc\"  ", null);
        Assert.Equal("abc", ifMatch);
        Assert.False(star);
    }

    [Fact]
    public void ParsePreconditions_treats_star_if_match_as_no_tag()
    {
        var (ifMatch, _) = DavProtocol.ParsePreconditions("*", null);
        Assert.Null(ifMatch);
    }

    [Fact]
    public void ParsePreconditions_detects_if_none_match_star()
    {
        Assert.True(DavProtocol.ParsePreconditions(null, "*").IfNoneMatchStar);
        Assert.False(DavProtocol.ParsePreconditions(null, "\"x\"").IfNoneMatchStar);
        Assert.Equal((null, false), DavProtocol.ParsePreconditions(null, null));
    }
}
