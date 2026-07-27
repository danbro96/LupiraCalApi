using System.Text.Json.Serialization;

namespace LupiraCalApi.Domain;

/// <summary>A field is fully present, weak/partial (0.5), or absent.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GapSeverity>))]
public enum GapSeverity { Weak, Absent }

/// <summary>A field the record is missing or thin on, with its rubric weight (heavier = ask first).</summary>
public sealed record CompletenessGap(string Field, double Weight, GapSeverity Severity);

/// <summary>How well-documented a record is: <c>Score</c> 0..1 (Σ weight·presence / Σ weight), the unmet
/// fields ranked by missing mass (weight·absence, largest first), and the rubric version that produced it.
/// <c>null</c> (not this type) means "not applicable".</summary>
public sealed record CompletenessScore(double Score, int RubricVersion, IReadOnlyList<CompletenessGap> Gaps);

/// <summary>
/// Pure, kind-aware completeness rubric for items. Scores <em>presence</em>, not quality — crude on purpose,
/// enough to rank thin-vs-rich. Time-agnostic: past and future items score alike; cutoffs are the caller's
/// time filters. Exempt records score <c>null</c>. Calendar-context exemption (Birthdays/Availability/system
/// calendars) and <paramref name="hasChildren"/> are decided by the caller and passed in; snapshot-local
/// exemptions (cancelled, a presence segment, a fired payload) are handled here.
/// </summary>
public static class CompletenessScorer
{
    public const int Version = 2;

    public static CompletenessScore? ScoreItem(CalendarItem item, bool calendarExempt, bool hasChildren = false)
    {
        if (calendarExempt || item.Status == ItemStatus.Cancelled
            || item.Details?.Presence is not null || item.Prompt is not null || item.Action is not null)
            return null;

        var fields = new List<(string Field, double Weight, double Presence)>();
        var d = item.Details;

        var category = item.Category ?? ItemCategory.General;   // an uncategorised timed item scores as a general event
        if (category == ItemCategory.Trip && hasChildren) category = ItemCategory.General;   // a parent trip's legs live on child items

        switch (category)
        {
            case ItemCategory.Trip:
                fields.Add(("fromToPlace", 2, TravelFromTo(d?.Travel)));
                fields.Add(("departArriveTimes", 1, Math.Max(BothTimes(item), LegTimes(d?.Travel))));
                fields.Add(("carrier", 1, Text(d?.Travel?.Carrier)));
                fields.Add(("booking", 1, Booking(d)));
                fields.Add(("seat", 0.5, Text(d?.Travel?.Seat)));
                break;

            case ItemCategory.Stay:
                fields.Add(("location", 2, Place(item)));
                fields.Add(("checkInOut", 1, BothTimes(item)));
                fields.Add(("booking", 1, Booking(d)));
                break;

            case ItemCategory.Appointment:
                fields.Add(("location", 2, Place(item)));
                fields.Add(("provider", 2, Has(d?.Booking?.ProviderContactId)));
                fields.Add(("time", 1, Time(item, allDayWeak: true)));
                break;

            case ItemCategory.Meal:
            case ItemCategory.Outing:
                fields.Add(("location", 2, Place(item)));
                fields.Add(("time", 1, Time(item, allDayWeak: true)));
                fields.Add(("booking", 1, Booking(d)));
                fields.Add(("attendees", 1, Attendees(item)));
                break;

            case ItemCategory.Occasion:
                fields.Add(("location", 2, Place(item)));
                fields.Add(("time", 1, Time(item)));   // occasions are legitimately all-day
                fields.Add(("description", 1, Description(item)));
                fields.Add(("attendees", 1, Attendees(item)));
                break;

            case ItemCategory.Meeting:
                fields.Add(("location", 2, Place(item)));
                fields.Add(("attendees", 2, Attendees(item)));
                fields.Add(("time", 1, Time(item, allDayWeak: true)));
                fields.Add(("description", 1, Description(item)));
                break;

            // General/Activity/Focus/Chore: a location/time/description cut (no attendees, so a
            // solo focus block or errand isn't penalised for missing them).
            default:
                fields.Add(("location", 2, Place(item)));
                fields.Add(("time", 1, Time(item)));
                fields.Add(("description", 1, Description(item)));
                break;
        }

        return Build(fields);
    }


    private static CompletenessScore Build(List<(string Field, double Weight, double Presence)> fields)
    {
        var totalWeight = fields.Sum(f => f.Weight);
        var score = totalWeight == 0 ? 1 : fields.Sum(f => f.Weight * f.Presence) / totalWeight;
        var gaps = fields
            .Where(f => f.Presence < 1)
            .OrderByDescending(f => f.Weight * (1 - f.Presence))
            .ThenByDescending(f => f.Weight)
            .Select(f => new CompletenessGap(f.Field, f.Weight, f.Presence == 0 ? GapSeverity.Absent : GapSeverity.Weak))
            .ToList();
        return new CompletenessScore(Math.Round(score, 4), Version, gaps);
    }

    // ---- presence helpers (1 present · 0.5 weak · 0 absent) ----

    private static double Place(CalendarItem i) => i.PlaceId is not null || !string.IsNullOrWhiteSpace(i.LocationLabel) ? 1 : 0;

    private static double Time(CalendarItem i, bool allDayWeak = false)
    {
        if (i.StartsAt is null && !(i.IsAllDay && i.StartDate is not null)) return 0;
        if (Fuzzy(i.StartPrecision)) return 0.5;
        return allDayWeak && i.StartsAt is null ? 0.5 : 1;   // clocked category on a date-only span → the time is the ask
    }

    private static double BothTimes(CalendarItem i)
    {
        if (i.StartsAt is not null && i.EndsAt is not null) return Fuzzy(i.StartPrecision) || Fuzzy(i.EndPrecision) ? 0.5 : 1;
        if (i.StartsAt is not null || i.EndsAt is not null) return 0.5;
        return i.IsAllDay && i.StartDate is not null && i.EndDate is not null ? 0.5 : 0;   // dates known, clock times are the ask
    }

    private static double LegTimes(TravelLeg? t) =>
        (t?.DepartAt is not null, t?.ArriveAt is not null) switch { (true, true) => 1, (false, false) => 0, _ => 0.5 };

    private static bool Fuzzy(DatePrecision? p) => p is DatePrecision.Month or DatePrecision.Year or DatePrecision.Approximate;

    private static double Description(CalendarItem i)
    {
        if (string.IsNullOrWhiteSpace(i.Description)) return 0;
        return string.Equals(i.Description.Trim(), i.Title?.Trim(), StringComparison.OrdinalIgnoreCase) ? 0.5 : 1;   // echoes the title → weak
    }

    private static double Attendees(CalendarItem i)
    {
        if (i.Attendees.Count == 0) return 0;
        return i.Attendees.All(a => a.Status == ParticipationStatus.NeedsAction) ? 0.5 : 1;   // listed but none RSVP'd → weak
    }

    private static double TravelFromTo(TravelLeg? t)
    {
        if (t is null) return 0;
        var to = t.ToPlaceId is not null || !string.IsNullOrWhiteSpace(t.ToLabel);
        var from = t.FromPlaceId is not null || !string.IsNullOrWhiteSpace(t.FromLabel);
        return (from, to) switch { (true, true) => 1, (false, false) => 0, _ => 0.5 };
    }

    private static double Booking(ItemDetails? d)
    {
        if (d?.Booking is not { } b) return 0;
        if (Text(b.ConfirmationNumber) == 1 || Text(b.Reference) == 1) return 1;
        // some reservation signal without a confirmation → weak, not absent
        return b.ProviderContactId is not null || Text(b.Url) == 1 || b.Amount is not null || b.PartySize is not null ? 0.5 : 0;
    }

    private static double Has(Guid? id) => id is not null ? 1 : 0;
    private static double Text(string? s) => string.IsNullOrWhiteSpace(s) ? 0 : 1;

}
