using System.Text.Json.Serialization;

namespace LupiraCalApi.Domain;

/// <summary>A field is fully present, weak/partial (0.5), or absent.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GapSeverity>))]
public enum GapSeverity { Weak, Absent }

/// <summary>A field the record is missing or thin on, with its rubric weight (heavier = ask first).</summary>
public sealed record CompletenessGap(string Field, double Weight, GapSeverity Severity);

/// <summary>How well-documented a record is: <c>Score</c> 0..1 (Σ weight·presence / Σ weight), the unmet
/// fields ranked heaviest-first, and the rubric version that produced it. <c>null</c> (not this type) means "not applicable".</summary>
public sealed record CompletenessScore(double Score, int RubricVersion, IReadOnlyList<CompletenessGap> Gaps);

/// <summary>
/// Pure, kind-aware completeness rubric for items and contacts. Scores <em>presence</em>, not quality — crude on purpose,
/// enough to rank thin-vs-rich. Exempt records score <c>null</c>. Calendar-context exemption (Birthdays/Availability/system
/// calendars) is decided by the caller and passed in; snapshot-local exemptions (a presence segment, a fired payload)
/// are handled here.
/// </summary>
public static class CompletenessScorer
{
    public const int Version = 1;

    public static CompletenessScore? ScoreItem(CalendarItem item, bool calendarExempt)
    {
        if (calendarExempt || item.Details?.Presence is not null || item.Prompt is not null || item.Action is not null)
            return null;

        var fields = new List<(string Field, double Weight, double Presence)>();
        var d = item.Details;

        switch (item.Category ?? ItemCategory.General)   // an uncategorised timed item scores as a general event
        {
            case ItemCategory.Trip:
                fields.Add(("fromToPlace", 2, TravelFromTo(d?.Travel)));
                fields.Add(("departArriveTimes", 1, BothTimes(item)));
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
                fields.Add(("time", 1, Time(item)));
                break;

            case ItemCategory.Meal:
            case ItemCategory.Outing:
                fields.Add(("location", 2, Place(item)));
                fields.Add(("time", 1, Time(item)));
                fields.Add(("booking", 1, Booking(d)));
                break;

            case ItemCategory.Meeting:
                fields.Add(("location", 2, Place(item)));
                fields.Add(("attendees", 2, Attendees(item)));
                fields.Add(("time", 1, Time(item)));
                fields.Add(("description", 1, Description(item)));
                break;

            // General/Occasion/Activity/Focus/Chore: a location/time/description cut (no attendees, so a
            // solo focus block or errand isn't penalised for missing them).
            default:
                fields.Add(("location", 2, Place(item)));
                fields.Add(("time", 1, Time(item)));
                fields.Add(("description", 1, Description(item)));
                break;
        }

        return Build(fields);
    }

    public static CompletenessScore? ScoreContact(Contact c, bool hasOrganisation)
    {
        var fields = new List<(string, double, double)>
        {
            ("name", 1, Name(c)),
            ("primaryReach", 3, AnyReach(c) ? 1 : 0),
            ("secondaryReach", 1, ReachCount(c) >= 2 ? 1 : 0),
            ("birthday", 1, c.Birthday is not null ? 1 : 0),
            ("postalAddress", 1, c.Addresses.Count > 0 ? 1 : 0),
            ("organisation", 1, hasOrganisation ? 1 : 0),
        };
        return Build(fields);
    }

    private static CompletenessScore Build(List<(string Field, double Weight, double Presence)> fields)
    {
        var totalWeight = fields.Sum(f => f.Weight);
        var score = totalWeight == 0 ? 1 : fields.Sum(f => f.Weight * f.Presence) / totalWeight;
        var gaps = fields
            .Where(f => f.Presence < 1)
            .OrderByDescending(f => f.Weight)
            .Select(f => new CompletenessGap(f.Field, f.Weight, f.Presence == 0 ? GapSeverity.Absent : GapSeverity.Weak))
            .ToList();
        return new CompletenessScore(Math.Round(score, 4), Version, gaps);
    }

    // ---- presence helpers (1 present · 0.5 weak · 0 absent) ----

    private static double Place(CalendarItem i) => i.PlaceId is not null ? 1 : 0;
    private static double Time(CalendarItem i) => i.StartsAt is not null || (i.IsAllDay && i.StartDate is not null) ? 1 : 0;
    private static double BothTimes(CalendarItem i) => (i.StartsAt is not null, i.EndsAt is not null) switch { (true, true) => 1, (false, false) => 0, _ => 0.5 };

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
        var to = t.ToPlaceId != Guid.Empty;
        var from = t.FromPlaceId is not null;
        return (from, to) switch { (true, true) => 1, (false, false) => 0, _ => 0.5 };
    }

    private static double Booking(ItemDetails? d) =>
        d?.Booking is { } b && (Text(b.ConfirmationNumber) == 1 || Text(b.Reference) == 1) ? 1 : 0;

    private static double Has(Guid? id) => id is not null ? 1 : 0;
    private static double Text(string? s) => string.IsNullOrWhiteSpace(s) ? 0 : 1;

    private static double Name(Contact c) =>
        !string.IsNullOrWhiteSpace(c.GivenName) || !string.IsNullOrWhiteSpace(c.FamilyName) || !string.IsNullOrWhiteSpace(c.Nickname) ? 1 : 0;

    private static bool AnyReach(Contact c) => ReachCount(c) >= 1;
    private static int ReachCount(Contact c) =>
        (c.Emails?.Length ?? 0) + (c.Phones?.Length ?? 0) + c.Profiles.Count;
}
