using System.Globalization;
using LupiraCalApi.Core.Serialization;
using NodaTime;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>The authored VTIMEZONE must predict every offset change NodaTime knows for the next two decades — a client
/// with no tz database of its own expands zoned series from it alone.</summary>
public class VTimeZoneBuilderTests
{
    [Theory]
    [InlineData("Europe/Stockholm")]
    [InlineData("Europe/London")]
    [InlineData("Europe/Dublin")]
    [InlineData("Europe/Tallinn")]
    [InlineData("America/New_York")]
    [InlineData("Australia/Sydney")]
    [InlineData("Pacific/Auckland")]
    [InlineData("Asia/Tokyo")]
    [InlineData("UTC")]
    public void Observances_predict_every_transition_nodatime_knows(string tz)
    {
        var zone = DateTimeZoneProviders.Tzdb[tz];
        foreach (var fromYear in new[] { 2012, 2019, 2026 })
        {
            var text = VTimeZoneBuilder.Build(zone, fromYear);
            Assert.NotNull(text);
            Assert.Contains($"TZID:{tz}", text);
            foreach (var o in Observances(text))
            {
                for (var year = fromYear; year < fromYear + 20; year++)
                {
                    if (o.Rule is null)
                    {
                        Assert.Equal(o.To, zone.GetUtcOffset(Instant.FromUtc(year, 6, 1, 0, 0)));
                        continue;
                    }

                    var at = o.OnsetIn(year).InZoneStrictly(DateTimeZone.ForOffset(o.From)).ToInstant();
                    Assert.Equal(o.From, zone.GetUtcOffset(at - Duration.FromMinutes(1)));
                    Assert.Equal(o.To, zone.GetUtcOffset(at));
                }
            }
        }
    }

    private sealed record Observance(LocalDateTime Onset, int? Month, int? Nth, IsoDayOfWeek? Day, Offset From, Offset To, string? Rule)
    {
        public LocalDateTime OnsetIn(int year)
        {
            var date = Nth == -1
                ? new LocalDate(year, Month!.Value, CalendarSystem.Iso.GetDaysInMonth(year, Month.Value)).With(DateAdjusters.PreviousOrSame(Day!.Value))
                : new LocalDate(year, Month!.Value, 1).With(DateAdjusters.NextOrSame(Day!.Value)).PlusWeeks(Nth!.Value - 1);
            return date + Onset.TimeOfDay;
        }
    }

    private static IEnumerable<Observance> Observances(string vtimezone)
    {
        foreach (var block in vtimezone.Split("BEGIN:").Where(b => b.StartsWith("STANDARD") || b.StartsWith("DAYLIGHT")))
        {
            var lines = block.Split("\r\n").Where(l => l.Contains(':') && !l.StartsWith("END:")).ToDictionary(l => l[..l.IndexOf(':')], l => l[(l.IndexOf(':') + 1)..]);
            var onset = LocalDateTime.FromDateTime(DateTime.ParseExact(lines["DTSTART"], "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture));
            var rule = lines.GetValueOrDefault("RRULE");
            int? month = null, nth = null;
            IsoDayOfWeek? day = null;
            if (rule is not null)
            {
                var parts = rule.Split(';').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
                month = int.Parse(parts["BYMONTH"], CultureInfo.InvariantCulture);
                nth = int.Parse(parts["BYDAY"][..^2], CultureInfo.InvariantCulture);
                day = parts["BYDAY"][^2..] switch
                {
                    "MO" => IsoDayOfWeek.Monday, "TU" => IsoDayOfWeek.Tuesday, "WE" => IsoDayOfWeek.Wednesday, "TH" => IsoDayOfWeek.Thursday,
                    "FR" => IsoDayOfWeek.Friday, "SA" => IsoDayOfWeek.Saturday, _ => IsoDayOfWeek.Sunday,
                };
            }

            yield return new Observance(onset, month, nth, day, ParseOffset(lines["TZOFFSETFROM"]), ParseOffset(lines["TZOFFSETTO"]), rule);
        }
    }

    private static Offset ParseOffset(string s)
    {
        var sign = s[0] == '-' ? -1 : 1;
        var seconds = int.Parse(s[1..3], CultureInfo.InvariantCulture) * 3600 + int.Parse(s[3..5], CultureInfo.InvariantCulture) * 60
            + (s.Length > 5 ? int.Parse(s[5..7], CultureInfo.InvariantCulture) : 0);
        return Offset.FromSeconds(sign * seconds);
    }
}
