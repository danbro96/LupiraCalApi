using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json;

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
/// calendars), <paramref name="hasChildren"/>, and <paramref name="inheritedAttendees"/> (the parent's attendee
/// presence — a trip's shared list covers its legs) are decided by the caller and passed in; snapshot-local
/// exemptions (cancelled, a presence segment, a fired payload) are handled here. A field acknowledged as
/// inapplicable via metadata <c>completeness.na</c> (e.g. no booking for a homemade dinner) is dropped from
/// the rubric entirely.
/// </summary>
public static class CompletenessScorer
{
    public const int Version = 2;

    public static CompletenessScore? ScoreItem(CalendarItem item, bool calendarExempt, bool hasChildren = false, double inheritedAttendees = 0)
    {
        if (calendarExempt || item.Status == ItemStatus.Cancelled
            || item.Details?.Presence is not null || item.Prompt is not null || item.Action is not null)
            return null;

        var fields = new List<(string Field, double Weight, double Presence)>();
        var d = item.Details;
        var attendees = Math.Max(AttendeePresence(item), inheritedAttendees);

        var category = item.Category ?? ItemCategory.General;   // an uncategorised timed item scores as a general event
        if (category == ItemCategory.Trip && hasChildren) category = ItemCategory.General;   // a parent trip's legs live on child items

        switch (category)
        {
            case ItemCategory.Trip:
                fields.Add(("fromToPlace", 2, TravelFromTo(d?.Travel)));
                fields.Add(("departArriveTimes", 1, Math.Max(BothTimes(item), LegTimes(d?.Travel))));
                if (d?.Travel is { } leg)
                {
                    // Mode-scoped logistics: only fields the mode can actually have. No leg → mode unknown,
                    // and the absent fromToPlace gap already carries the "add travel details" ask.
                    if (leg.Mode is TransportMode.Flight or TransportMode.Train or TransportMode.Metro
                        or TransportMode.Tram or TransportMode.Bus or TransportMode.Coach or TransportMode.Ferry)
                        fields.Add(("carrier", 1, Math.Max(Text(leg.Carrier), Text(leg.ServiceNumber))));
                    if (leg.Mode is TransportMode.Flight or TransportMode.Train or TransportMode.Coach or TransportMode.Ferry)
                        fields.Add(("seat", 0.5, Text(leg.Seat)));
                    if (leg.Mode == TransportMode.Car)
                        fields.Add(("driver", 1, Has(leg.DriverContactId)));
                }
                fields.Add(("booking", 1, Booking(d)));
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
                fields.Add(("description", 1, Description(item)));
                break;

            case ItemCategory.Meal:
            case ItemCategory.Outing:
                fields.Add(("location", 2, Place(item)));
                fields.Add(("time", 1, Time(item, allDayWeak: true)));
                fields.Add(("booking", 1, Booking(d)));
                fields.Add(("attendees", 1, attendees));
                break;

            case ItemCategory.Occasion:
                fields.Add(("location", 2, Place(item)));
                fields.Add(("time", 1, Time(item)));   // occasions are legitimately all-day
                fields.Add(("description", 1, Description(item)));
                fields.Add(("attendees", 1, attendees));
                break;

            case ItemCategory.Meeting:
                fields.Add(("location", 2, Place(item)));
                fields.Add(("attendees", 2, attendees));
                fields.Add(("time", 1, Time(item, allDayWeak: true)));
                fields.Add(("description", 1, Description(item)));
                break;

            // General/Activity/Focus/Chore: a location/time/description cut (no attendees, so a
            // solo focus block or errand isn't penalised for missing them).
            default:
                if (item.Category is null) fields.Add(("category", 1, 0));   // the first ask — the category unlocks the right rubric
                fields.Add(("location", 2, Place(item)));
                fields.Add(("time", 1, Time(item)));
                fields.Add(("description", 1, Description(item)));
                break;
        }

        var na = NaFields(item.Metadata);
        if (na.Count > 0) fields.RemoveAll(f => na.Contains(f.Field));

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

    /// <summary>Rubric fields the user acknowledged as inapplicable: metadata <c>{"completeness":{"na":["booking"]}}</c>.</summary>
    private static HashSet<string> NaFields(string metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return [];
        try
        {
            if (JsonNode.Parse(metadata)?["completeness"]?["na"] is not JsonArray na) return [];
            return new HashSet<string>(
                na.Select(n => n?.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : null).OfType<string>(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { return []; }
    }

    // ---- presence helpers (1 present · 0.5 weak · 0 absent) ----

    private static double Place(CalendarItem i) =>
        i.PlaceId is not null ? 1 : !string.IsNullOrWhiteSpace(i.LocationLabel) ? 0.5 : 0;   // a raw label geo didn't resolve → weak

    private static double Time(CalendarItem i, bool allDayWeak = false)
    {
        if (i.StartsAt is null && !(i.IsAllDay && i.StartDate is not null)) return 0;
        if (Fuzzy(i.StartPrecision)) return 0.5;
        if (i.StartsAt is null) return allDayWeak ? 0.5 : 1;   // date-only span; clocked categories still want the clock time
        return i.EndsAt is null ? 0.75 : 1;   // start known, duration open
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
        var text = i.Description.Trim();
        if (string.Equals(text, i.Title?.Trim(), StringComparison.OrdinalIgnoreCase)) return 0.5;   // echoes the title → weak
        return text.Length < 20 ? 0.5 : 1;   // a few characters add little over the title
    }

    internal static double AttendeePresence(CalendarItem i)
    {
        if (i.Attendees.Count == 0) return 0;
        return i.Attendees.All(a => a.Status == ParticipationStatus.NeedsAction) ? 0.5 : 1;   // listed but none RSVP'd → weak
    }

    private static double TravelFromTo(TravelLeg? t) =>
        t is null ? 0 : (Endpoint(t.FromPlaceId, t.FromLabel) + Endpoint(t.ToPlaceId, t.ToLabel)) / 2;

    private static double Endpoint(Guid? placeId, string? label) =>
        placeId is not null ? 1 : !string.IsNullOrWhiteSpace(label) ? 0.5 : 0;

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
