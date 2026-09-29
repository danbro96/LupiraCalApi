using System.Text.Json;
using System.Text.Json.Serialization;
using LupiraCalApi.Core.Data;
using LupiraCalApi.Core.Domain.CalendarItems;
using Marten;

// Emits parity fixtures for the mobile domain package (packages/domain in the LupiraCal monorepo):
//   <out-dir>/recurrence.json   — recurrence-rule expansions computed by the server's RecurrenceExpander;
//                                 the TS expander must reproduce `expected` exactly (UTC, half-open window).
//   <out-dir>/lww-vectors.json  — SectionLww wins/tiebreak decisions; the client reducer must agree on every row.
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
Add("zoned-until-reads-as-wall-clock", "FREQ=DAILY;UNTIL=20261027T170000Z", Utc(2026, 10, 23, 16), 60, Utc(2026, 10, 1, 0), Utc(2026, 11, 1, 0), "Europe/Stockholm");
Add("zoned-count", "FREQ=WEEKLY;COUNT=4", Utc(2026, 10, 11, 16), 60, Utc(2026, 10, 1, 0), Utc(2026, 12, 1, 0), "Europe/Stockholm");
Add("zoned-monthly-second-tuesday", "FREQ=MONTHLY;BYDAY=2TU", Utc(2026, 1, 13, 23), 60, Utc(2026, 1, 1, 0), Utc(2026, 7, 1, 0), "America/New_York");
Add("zoned-byday-is-local-weekday", "FREQ=WEEKLY;BYDAY=MO", Utc(2026, 1, 4, 23), 60, Utc(2026, 1, 1, 0), Utc(2026, 2, 1, 0), "Asia/Tokyo");
Add("zoned-window-clips-mid-series", "FREQ=DAILY", Utc(2026, 10, 20, 16), 60, Utc(2026, 10, 24, 12), Utc(2026, 10, 27, 12), "Europe/Stockholm");
Add("unknown-zone-is-utc", "FREQ=WEEKLY", Utc(2026, 10, 11, 16), 60, Utc(2026, 10, 1, 0), Utc(2026, 11, 15, 0), "Not/AZone");

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
Vec("seq-fallback-order", t, SectionLww.FromSequence(10), t, SectionLww.FromSequence(9));
Vec("seq-fallback-hex-boundary", t, SectionLww.FromSequence(16), t, SectionLww.FromSequence(15));

await File.WriteAllTextAsync(Path.Combine(outDir, "lww-vectors.json"), JsonSerializer.Serialize(vectors, json));
Console.WriteLine($"lww-vectors.json: {vectors.Count} vectors → {outDir}");
return 0;
