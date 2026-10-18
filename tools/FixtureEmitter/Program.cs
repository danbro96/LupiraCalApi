using System.Text.Json;
using System.Text.Json.Serialization;
using Lupira.Primitives;
using LupiraCalApi.Core.Data;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using Marten;

// Emits parity fixtures for the mobile domain package (packages/domain in the LupiraCal monorepo):
//   <out-dir>/recurrence.json     — recurrence-rule expansions computed by the server's RecurrenceExpander;
//                                   the TS expander must reproduce `expected` exactly (UTC, half-open window).
//   <out-dir>/series.json         — occurrences of series with excluded, extra and changed occurrences (RecurrenceExpander.Occurrences).
//   <out-dir>/series-follow.json  — where a series' end and deviations land when the series moves (KeepingBoundOf + SeriesDeviations.Follow).
//   <out-dir>/split.json          — a series cut at an occurrence (SeriesSplit), with the continuation's derived ids.
//   <out-dir>/lww-vectors.json    — SectionLww wins/tiebreak decisions; the client reducer must agree on every row.
//
//   dotnet run --project tools/FixtureEmitter -- <out-dir> [--from-db <connection-string>]
//
// --from-db additionally expands every distinct recurrenceRule found in a live store, so the corpus covers
// real family data, not just the curated cases.

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: FixtureEmitter <out-dir> [--from-db <connection-string>]");
    return 1;
}
var outDir = Path.GetFullPath(args[0]);
Directory.CreateDirectory(outDir);
var json = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

var expander = new RecurrenceExpander();
var cases = new List<RecurrenceCase>();

void Add(string name, string rule, DateTimeOffset start, int durationMinutes, DateTimeOffset windowStart, DateTimeOffset windowEnd, string? timeZone = null)
{
    var item = new CalendarItem
    {
        Id = Guid.NewGuid(),
        ExternalId = $"fixture-{name}",
        Title = $"Fixture {name}",
        StartsAt = start,
        EndsAt = start.AddMinutes(durationMinutes),
        RecurrenceRule = rule,
        StartTimezone = timeZone,
    };
    cases.Add(new RecurrenceCase(
        name, rule, start, durationMinutes, windowStart, windowEnd,
        [.. expander.Expand(item, windowStart, windowEnd)], timeZone));
}

var mon = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);   // a Monday
var win = (From: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), To: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

Add("daily", "FREQ=DAILY", mon, 60, win.From, mon.AddDays(7));
Add("daily-count", "FREQ=DAILY;COUNT=3", mon, 60, win.From, win.To);
Add("daily-interval", "FREQ=DAILY;INTERVAL=3", mon, 60, win.From, mon.AddDays(14));
Add("daily-until", "FREQ=DAILY;UNTIL=20260110T090000Z", mon, 60, win.From, win.To);
Add("weekly", "FREQ=WEEKLY", mon, 60, win.From, win.To);
Add("weekly-byday", "FREQ=WEEKLY;BYDAY=MO,WE,FR", mon, 60, win.From, mon.AddDays(21));
Add("weekly-interval-byday", "FREQ=WEEKLY;INTERVAL=2;BYDAY=TU,TH", mon, 60, win.From, win.To);
Add("weekly-wkst-sunday", "FREQ=WEEKLY;INTERVAL=2;BYDAY=SU;WKST=SU", mon.AddDays(-1), 60, win.From, win.To);
Add("monthly-bymonthday", "FREQ=MONTHLY;BYMONTHDAY=15", new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), 30, win.From, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
Add("monthly-second-tuesday", "FREQ=MONTHLY;BYDAY=2TU", new DateTimeOffset(2026, 1, 13, 18, 0, 0, TimeSpan.Zero), 60, win.From, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
Add("monthly-last-friday", "FREQ=MONTHLY;BYDAY=-1FR", new DateTimeOffset(2026, 1, 30, 17, 0, 0, TimeSpan.Zero), 60, win.From, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
Add("monthly-31st-skips-short-months", "FREQ=MONTHLY;BYMONTHDAY=31", new DateTimeOffset(2026, 1, 31, 8, 0, 0, TimeSpan.Zero), 30, win.From, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
Add("yearly", "FREQ=YEARLY", new DateTimeOffset(2026, 3, 14, 10, 0, 0, TimeSpan.Zero), 60, win.From, new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
Add("yearly-thanksgiving", "FREQ=YEARLY;BYMONTH=11;BYDAY=4TH", new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero), 60, win.From, new DateTimeOffset(2029, 1, 1, 0, 0, 0, TimeSpan.Zero));
Add("yearly-leap-feb29", "FREQ=YEARLY", new DateTimeOffset(2024, 2, 29, 9, 0, 0, TimeSpan.Zero), 60, new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
Add("window-clips-mid-series", "FREQ=DAILY", mon, 60, mon.AddDays(3), mon.AddDays(6));
Add("infinite-rule-bounded-window", "FREQ=DAILY;INTERVAL=1", mon, 60, mon, mon.AddDays(3));

// Zoned series keep their wall-clock time across offset changes.
DateTimeOffset Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, TimeSpan.Zero);
Add("zoned-weekly-autumn-shift", "FREQ=WEEKLY", Utc(2026, 10, 11, 16), 60, Utc(2026, 10, 1, 0), Utc(2026, 11, 15, 0), "Europe/Stockholm");
Add("zoned-daily-spring-gap", "FREQ=DAILY", Utc(2026, 3, 27, 1, 30), 30, Utc(2026, 3, 26, 0), Utc(2026, 4, 1, 0), "Europe/Stockholm");
Add("zoned-daily-autumn-ambiguous", "FREQ=DAILY", Utc(2026, 10, 23, 0, 30), 30, Utc(2026, 10, 22, 0), Utc(2026, 10, 28, 0), "Europe/Stockholm");
Add("zoned-until-is-an-instant", "FREQ=DAILY;UNTIL=20261027T170000Z", Utc(2026, 10, 23, 16), 60, Utc(2026, 10, 1, 0), Utc(2026, 11, 1, 0), "Europe/Stockholm");
Add("zoned-until-is-an-instant-west", "FREQ=DAILY;UNTIL=20261027T200000Z", Utc(2026, 10, 24, 22), 60, Utc(2026, 10, 1, 0), Utc(2026, 11, 1, 0), "America/New_York");
Add("zoned-floating-until-reads-the-wall-clock", "FREQ=DAILY;UNTIL=20261027T180000", Utc(2026, 10, 23, 16), 60, Utc(2026, 10, 1, 0), Utc(2026, 11, 1, 0), "Europe/Stockholm");
Add("zoned-date-until-covers-its-day", "FREQ=DAILY;UNTIL=20261027", Utc(2026, 10, 23, 16), 60, Utc(2026, 10, 1, 0), Utc(2026, 11, 1, 0), "Europe/Stockholm");
Add("zoned-count", "FREQ=WEEKLY;COUNT=4", Utc(2026, 10, 11, 16), 60, Utc(2026, 10, 1, 0), Utc(2026, 12, 1, 0), "Europe/Stockholm");
Add("zoned-monthly-second-tuesday", "FREQ=MONTHLY;BYDAY=2TU", Utc(2026, 1, 13, 23), 60, Utc(2026, 1, 1, 0), Utc(2026, 7, 1, 0), "America/New_York");
Add("zoned-byday-is-local-weekday", "FREQ=WEEKLY;BYDAY=MO", Utc(2026, 1, 4, 23), 60, Utc(2026, 1, 1, 0), Utc(2026, 2, 1, 0), "Asia/Tokyo");
Add("zoned-window-clips-mid-series", "FREQ=DAILY", Utc(2026, 10, 20, 16), 60, Utc(2026, 10, 24, 12), Utc(2026, 10, 27, 12), "Europe/Stockholm");
Add("unknown-zone-is-utc", "FREQ=WEEKLY", Utc(2026, 10, 11, 16), 60, Utc(2026, 10, 1, 0), Utc(2026, 11, 15, 0), "Not/AZone");
Add("daily-decades-in", "FREQ=DAILY", mon, 60, Utc(2056, 1, 1, 0), Utc(2056, 1, 8, 0));
Add("zoned-daily-decades-in", "FREQ=DAILY", Utc(2026, 1, 5, 8), 60, Utc(2056, 3, 23, 0), Utc(2056, 3, 30, 0), "Europe/Stockholm");
Add("weekdays-count-past-ten-thousand", "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR;COUNT=10010", mon, 60, Utc(2064, 4, 20, 0), Utc(2064, 6, 1, 0));

// Real-store corpus: every distinct rule in a live database, expanded over a canonical window.
var fromDb = Array.IndexOf(args, "--from-db");
if (fromDb >= 0 && fromDb + 1 < args.Length)
{
    using var store = DocumentStore.For(opts =>
    {
        opts.Connection(args[fromDb + 1]);
        opts.UseLupiraCal();
    });
    await using var session = store.QuerySession();
    var items = await session.Query<CalendarItem>()
        .Where(i => i.RecurrenceRule != null && i.DeletedAt == null).ToListAsync();
    var n = 0;
    foreach (var group in items.GroupBy(i => i.RecurrenceRule))
    {
        var sample = group.First();
        if (sample.StartsAt is not { } start) continue;
        Add($"db-{n++:D3}", group.Key!, start, (int) ((sample.EndsAt - sample.StartsAt)?.TotalMinutes ?? 60),
            start.AddMonths(-1), start.AddMonths(6));
    }
    Console.WriteLine($"Ingested {n} distinct rules from the store.");
}

await File.WriteAllTextAsync(Path.Combine(outDir, "recurrence.json"), JsonSerializer.Serialize(cases, json));
Console.WriteLine($"recurrence.json: {cases.Count} cases → {outDir}");

// ---- Series with deviations: every occurrence keeps the start it has in the unmodified series. ----

OccurrenceOverride Change(DateTimeOffset originalStart, DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null, string? title = null, ItemStatus? status = null) =>
    new(originalStart, startsAt, endsAt, title, null, status, null);
SeriesShape Timed(DateTimeOffset start, int minutes, string rule, string? zone = "UTC") => new(false, start, start.AddMinutes(minutes), null, null, zone, rule);
SeriesShape AllDay(DateOnly start, DateOnly end, string rule) => new(true, null, null, start, end, null, rule);

var seriesCases = new List<SeriesCase>();
void Series(string name, SeriesShape item, DateTimeOffset windowStart, DateTimeOffset windowEnd) =>
    seriesCases.Add(new SeriesCase(name, item, windowStart, windowEnd, expander.Occurrences(item.ToItem(), windowStart, windowEnd)));

var jul = Utc(2026, 7, 1, 9);
Series("exclude-add-move-cancel-and-pull-in", Timed(jul, 60, "FREQ=WEEKLY") with
{
    ExcludedOccurrences = [Utc(2026, 7, 8, 9)],
    ExtraOccurrences = [Utc(2026, 7, 10, 12)],
    OccurrenceOverrides = [Change(Utc(2026, 7, 15, 9), Utc(2026, 7, 15, 14), title: "Moved"), Change(Utc(2026, 7, 22, 9), status: ItemStatus.Cancelled),
        Change(Utc(2026, 7, 29, 9), Utc(2026, 7, 28, 9))],
}, Utc(2026, 7, 1, 0), Utc(2026, 7, 29, 0));
Series("excluded-extra", Timed(jul, 60, "FREQ=WEEKLY;COUNT=2") with { ExtraOccurrences = [Utc(2026, 7, 3, 12)], ExcludedOccurrences = [Utc(2026, 7, 3, 12)] },
    Utc(2026, 7, 1, 0), Utc(2026, 8, 1, 0));
Series("moved-onto-another-start", Timed(jul, 60, "FREQ=WEEKLY;COUNT=3") with { OccurrenceOverrides = [Change(Utc(2026, 7, 8, 9), Utc(2026, 7, 15, 9), title: "Moved")] },
    Utc(2026, 7, 1, 0), Utc(2026, 8, 1, 0));
Series("moved-out-of-the-window", Timed(jul, 60, "FREQ=WEEKLY") with { OccurrenceOverrides = [Change(Utc(2026, 7, 8, 9), Utc(2026, 8, 20, 9))] },
    Utc(2026, 7, 1, 0), Utc(2026, 7, 20, 0));
Series("change-keyed-off-the-series", Timed(jul, 60, "FREQ=WEEKLY;COUNT=2") with { OccurrenceOverrides = [Change(Utc(2026, 7, 2, 9), Utc(2026, 7, 3, 9))] },
    Utc(2026, 7, 1, 0), Utc(2026, 8, 1, 0));
Series("change-on-an-extra", Timed(jul, 60, "FREQ=WEEKLY;COUNT=2") with
{
    ExtraOccurrences = [Utc(2026, 7, 3, 12)], OccurrenceOverrides = [Change(Utc(2026, 7, 3, 12), Utc(2026, 7, 4, 12), title: "Extra moved")],
}, Utc(2026, 7, 1, 0), Utc(2026, 8, 1, 0));
Series("zoned-across-the-autumn-shift", Timed(Utc(2026, 10, 4, 16), 180, "FREQ=WEEKLY;BYDAY=SU", "Europe/Stockholm") with
{
    ExcludedOccurrences = [Utc(2026, 11, 1, 17)], OccurrenceOverrides = [Change(Utc(2026, 10, 18, 16), Utc(2026, 10, 19, 16), Utc(2026, 10, 19, 19), "Monday")],
}, Utc(2026, 10, 1, 0), Utc(2026, 11, 16, 0));
Series("all-day-with-a-longer-occurrence", AllDay(new DateOnly(2026, 7, 27), new DateOnly(2026, 7, 28), "FREQ=WEEKLY;COUNT=3") with
{
    OccurrenceOverrides = [Change(Utc(2026, 8, 3, 0), Utc(2026, 8, 4, 0), Utc(2026, 8, 7, 0))],
}, Utc(2026, 7, 1, 0), Utc(2026, 9, 1, 0));

await File.WriteAllTextAsync(Path.Combine(outDir, "series.json"), JsonSerializer.Serialize(seriesCases, json));
Console.WriteLine($"series.json: {seriesCases.Count} cases → {outDir}");

// ---- Moving a series: its end and deviations follow their occurrences. ----

var followCases = new List<SeriesFollowCase>();
void Follow(string name, SeriesShape before, SeriesShape after)
{
    var moved = after.ToSeries().KeepingBoundOf(before.ToSeries());
    var deviations = SeriesDeviations.Of(before.ToItem()).Follow(before.ToSeries(), moved, before.ExtraOccurrences, expander);
    followCases.Add(new SeriesFollowCase(name, before, after, moved.RecurrenceRule, deviations.Excluded, deviations.Overrides));
}

var sundays = Timed(Utc(2026, 10, 4, 16), 180, "FREQ=WEEKLY;BYDAY=SU", "Europe/Stockholm") with
{
    ExcludedOccurrences = [Utc(2026, 10, 18, 16), Utc(2026, 11, 1, 17)],
    OccurrenceOverrides = [Change(Utc(2026, 10, 11, 16), title: "Renamed"), Change(Utc(2026, 10, 25, 17), Utc(2026, 10, 26, 17), Utc(2026, 10, 26, 19), status: ItemStatus.Tentative)],
};
Follow("new-time-across-the-autumn-shift", sundays, sundays with { StartsAt = Utc(2026, 10, 4, 17), EndsAt = Utc(2026, 10, 4, 20) });
Follow("new-day", sundays, sundays with { StartsAt = Utc(2026, 10, 5, 16), EndsAt = Utc(2026, 10, 5, 19), RecurrenceRule = "FREQ=WEEKLY;BYDAY=MO" });
Follow("new-zone-same-wall-clock", sundays, sundays with { StartsAt = Utc(2026, 10, 4, 17), EndsAt = Utc(2026, 10, 4, 20), StartTimezone = "Europe/London" });
Follow("rule-no-longer-has-them", sundays, sundays with { RecurrenceRule = "FREQ=MONTHLY;BYDAY=1SU" });
Follow("stops-recurring", sundays, sundays with { RecurrenceRule = null });
Follow("extra-stays-put", sundays with { ExtraOccurrences = [Utc(2026, 10, 14, 10)], ExcludedOccurrences = [Utc(2026, 10, 14, 10)] },
    sundays with { StartsAt = Utc(2026, 10, 4, 17), EndsAt = Utc(2026, 10, 4, 20) });
Follow("bounded-instant-moves-along", sundays with { RecurrenceRule = "FREQ=WEEKLY;BYDAY=SU;UNTIL=20261025T170000Z" },
    sundays with { StartsAt = Utc(2026, 10, 4, 17), EndsAt = Utc(2026, 10, 4, 20), RecurrenceRule = "FREQ=WEEKLY;BYDAY=SU;UNTIL=20261025T170000Z" });
Follow("bounded-wall-clock-moves-along", sundays with { RecurrenceRule = "FREQ=WEEKLY;BYDAY=SU;UNTIL=20261025T180000" },
    sundays with { StartsAt = Utc(2026, 10, 4, 17), EndsAt = Utc(2026, 10, 4, 20), RecurrenceRule = "FREQ=WEEKLY;BYDAY=SU;UNTIL=20261025T180000" });
Follow("bounded-count-is-kept", sundays with { RecurrenceRule = "FREQ=WEEKLY;BYDAY=SU;COUNT=5" },
    sundays with { StartsAt = Utc(2026, 10, 4, 17), EndsAt = Utc(2026, 10, 4, 20), RecurrenceRule = "FREQ=WEEKLY;BYDAY=SU;COUNT=5" });
Follow("new-rule-taken-as-written", sundays with { RecurrenceRule = "FREQ=WEEKLY;BYDAY=SU;UNTIL=20261025T170000Z" },
    sundays with { StartsAt = Utc(2026, 10, 4, 17), EndsAt = Utc(2026, 10, 4, 20), RecurrenceRule = "FREQ=WEEKLY;BYDAY=SU;UNTIL=20261101T170000Z" });
var saturdays = AllDay(new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 4), "FREQ=WEEKLY;UNTIL=20261031") with { ExcludedOccurrences = [Utc(2026, 10, 17, 0)] };
Follow("all-day-by-whole-days", saturdays, saturdays with { StartDate = new DateOnly(2026, 10, 4), EndDate = new DateOnly(2026, 10, 5) });

await File.WriteAllTextAsync(Path.Combine(outDir, "series-follow.json"), JsonSerializer.Serialize(followCases, json));
Console.WriteLine($"series-follow.json: {followCases.Count} cases → {outDir}");

// ---- Splitting a series at an occurrence (null parts: the start can't split it — not a later rule occurrence). ----

var splitCases = new List<SplitCase>();
void Split(string name, SeriesShape item, DateTimeOffset at)
{
    var split = SeriesSplit.At(item.ToItem(), at, expander);
    var sourceKey = $"split-{name}";
    var continuationId = DeterministicGuid.From(sourceKey);
    var participationId = DeterministicGuid.From($"participation-{name}");
    splitCases.Add(new SplitCase(name, item, at, split is null ? null : SeriesShape.Of(split.Earlier), split is null ? null : SeriesShape.Of(split.Following),
        sourceKey, continuationId, participationId, SeriesSplit.ContinuedParticipationId(continuationId, participationId)));
}

var mondays = Timed(Utc(2026, 7, 6, 9), 120, "FREQ=WEEKLY;BYDAY=MO");
Split("count", mondays with { RecurrenceRule = "FREQ=WEEKLY;BYDAY=MO;COUNT=5" }, Utc(2026, 7, 20, 9));
Split("open", mondays, Utc(2026, 7, 27, 9));
Split("until", Timed(Utc(2026, 7, 6, 9), 60, "FREQ=DAILY;UNTIL=20260715T090000Z"), Utc(2026, 7, 10, 9));
Split("all-day", AllDay(new DateOnly(2026, 7, 4), new DateOnly(2026, 7, 5), "FREQ=WEEKLY"), Utc(2026, 7, 18, 0));
Split("zoned-across-the-autumn-shift", Timed(Utc(2026, 10, 4, 16), 180, "FREQ=WEEKLY;BYDAY=SU", "Europe/Stockholm"), Utc(2026, 11, 1, 17));
Split("sub-second-start", Timed(Utc(2026, 7, 6, 9).AddMilliseconds(500), 60, "FREQ=DAILY"), Utc(2026, 7, 9, 9));
Split("deviations-go-with-their-part", mondays with
{
    ExcludedOccurrences = [Utc(2026, 7, 13, 9), Utc(2026, 8, 3, 9)],
    ExtraOccurrences = [Utc(2026, 7, 15, 12), Utc(2026, 7, 29, 12)],
    OccurrenceOverrides = [Change(Utc(2026, 7, 6, 9), title: "First"), Change(Utc(2026, 7, 27, 9), Utc(2026, 7, 19, 9), title: "Pulled earlier")],
}, Utc(2026, 7, 20, 9));
Split("first-occurrence", mondays, Utc(2026, 7, 6, 9));
Split("extra-only", mondays with { ExtraOccurrences = [Utc(2026, 7, 15, 12)] }, Utc(2026, 7, 15, 12));
Split("off-the-series", mondays, Utc(2026, 7, 14, 9));
Split("daily-20-years", Timed(Utc(2026, 7, 6, 9), 60, "FREQ=DAILY"), Utc(2046, 7, 6, 9));
Split("weekdays-40-years", Timed(Utc(2026, 7, 6, 9), 60, "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR"), Utc(2066, 7, 5, 9));
Split("weekdays-count-15000", Timed(Utc(2026, 7, 6, 9), 60, "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR;COUNT=15000"), Utc(2066, 7, 5, 9));
Split("monthly-31st", Timed(Utc(2026, 1, 31, 8), 30, "FREQ=MONTHLY;BYMONTHDAY=31"), Utc(2026, 8, 31, 8));
Split("yearly-feb-29", Timed(Utc(2024, 2, 29, 9), 60, "FREQ=YEARLY"), Utc(2032, 2, 29, 9));

await File.WriteAllTextAsync(Path.Combine(outDir, "split.json"), JsonSerializer.Serialize(splitCases, json));
Console.WriteLine($"split.json: {splitCases.Count} cases → {outDir}");

// ---- LWW vectors: every row's `wins` is computed by the server rule the client must mirror. ----

var t = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
Guid G(string tail) => Guid.Parse($"00000000-0000-0000-0000-{tail.PadLeft(12, '0')}");
var vectors = new List<LwwVector>();

void Vec(string name, DateTimeOffset occurredAt, Guid cmd, DateTimeOffset guardTs, Guid guardCmd) =>
    vectors.Add(new LwwVector(name, occurredAt, cmd, guardTs, guardCmd, SectionLww.Wins(occurredAt, cmd, guardTs, guardCmd)));

Vec("later-ts-wins", t.AddSeconds(1), G("1"), t, G("f"));
Vec("earlier-ts-loses", t.AddSeconds(-1), G("f"), t, G("1"));
Vec("equal-ts-higher-cmd-wins", t, G("b"), t, G("a"));
Vec("equal-ts-lower-cmd-loses", t, G("a"), t, G("b"));
Vec("equal-pair-replay-loses", t, G("c"), t, G("c"));
Vec("digit-vs-letter-ordinal", t, G("a00"), t, G("999"));           // '9' < 'a' ordinally, consistent with hex value
Vec("empty-guard-any-write-wins", t, Guid.Empty, DateTimeOffset.MinValue, Guid.Empty);
Vec("sub-ms-precision", t.AddTicks(1), G("1"), t, G("f"));
Vec("hex-length-boundary", t, G("10"), t, G("f"));

await File.WriteAllTextAsync(Path.Combine(outDir, "lww-vectors.json"), JsonSerializer.Serialize(vectors, json));
Console.WriteLine($"lww-vectors.json: {vectors.Count} vectors → {outDir}");
return 0;
