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
/// calendars) is decided by the caller and passed in; snapshot-local exemptions (the Availability kind, a fired payload)
/// are handled here.
/// </summary>
public static class CompletenessScorer
{
    public const int Version = 1;

    public static CompletenessScore? ScoreItem(CalendarItem item, bool calendarExempt)
    {
        if (calendarExempt || item.Kind == ItemKind.Availability || item.Prompt is not null || item.Action is not null)
            return null;

        var fields = new List<(string Field, double Weight, double Presence)>();
        var d = item.KindDetails;

        switch (item.Kind ?? ItemKind.Generic)   // an unkinded timed item scores as a generic meeting
        {
            case ItemKind.Appointment:
                fields.Add(("location", 2, Place(item)));
                fields.Add(("provider", 2, Has(d?.Appointment?.ProviderContactId)));
                fields.Add(("time", 1, Time(item)));
                fields.Add(("prepNotes", 1, Text(d?.Appointment?.PreparationNotes)));
                fields.Add(("reference", 1, Text(d?.Appointment?.ReferenceNumber)));
                break;

            case ItemKind.Flight:
                fields.Add(("flightNumber", 2, Text(d?.Flight?.FlightNumber)));
                fields.Add(("departArriveTimes", 1, BothTimes(item)));
                fields.Add(("gateTerminal", 1, Pair(d?.Flight?.Gate, d?.Flight?.Terminal)));
                fields.Add(("bookingRef", 1, Text(d?.Travel?.BookingReference)));
                fields.Add(("seat", 0.5, Text(d?.Flight?.SeatAssignment)));
                break;

            case ItemKind.Travel:
            case ItemKind.Train:
            case ItemKind.Bus:
                fields.Add(("fromToPlace", 2, FromTo(d?.Travel)));
                fields.Add(("departArriveTimes", 1, BothTimes(item)));
                fields.Add(("carrier", 1, Carrier(d)));
                fields.Add(("bookingRef", 1, Text(d?.Travel?.BookingReference)));
                fields.Add(("seat", 0.5, Text(d?.Train?.Seat ?? d?.Bus?.SeatReservation)));
                break;

            case ItemKind.Generic:
                fields.Add(("location", 2, Place(item)));
                fields.Add(("attendees", 2, Attendees(item)));
                fields.Add(("time", 1, Time(item)));
                fields.Add(("description", 1, Description(item)));
                break;

            // Car/Lodging/Ticketed/Delivery/Bill have no rubric in the doc — a generic location/time/description cut
            // (no attendees, so a bill/delivery isn't penalised for missing them).
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

    private static double Pair(string? a, string? b) => (!string.IsNullOrWhiteSpace(a), !string.IsNullOrWhiteSpace(b)) switch
    {
        (true, true) => 1,
        (false, false) => 0,
        _ => 0.5,
    };

    private static double FromTo(TravelDetail? t)
    {
        if (t is null) return 0;
        var to = t.ToPlaceId != Guid.Empty;
        var from = t.FromPlaceId is not null;
        return (from, to) switch { (true, true) => 1, (false, false) => 0, _ => 0.5 };
    }

    private static double Carrier(ItemKindDetails? d) =>
        Text(d?.Travel?.Carrier) == 1 || Text(d?.Train?.TrainNumber) == 1 || Text(d?.Bus?.Operator) == 1 ? 1 : 0;

    private static double Has(Guid? id) => id is not null ? 1 : 0;
    private static double Text(string? s) => string.IsNullOrWhiteSpace(s) ? 0 : 1;

    private static double Name(Contact c) =>
        !string.IsNullOrWhiteSpace(c.GivenName) || !string.IsNullOrWhiteSpace(c.FamilyName) || !string.IsNullOrWhiteSpace(c.Nickname) ? 1 : 0;

    private static bool AnyReach(Contact c) => ReachCount(c) >= 1;
    private static int ReachCount(Contact c) =>
        (c.Emails?.Length ?? 0) + (c.Phones?.Length ?? 0) + c.Profiles.Count;
}
