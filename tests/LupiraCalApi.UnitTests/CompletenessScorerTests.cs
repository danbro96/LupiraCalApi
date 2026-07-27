using LupiraCalApi.Domain;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class CompletenessScorerTests
{
    static ItemPrompt SamplePrompt() => new(
        PromptIntent.Monitor, null, "x", OutputKind.Summary, null, null, FallbackMode.Retry,
        new PromptFire(PromptFireKind.OnStart, null, null), true);

    [Fact]
    public void Exempt_calendar_scores_null()
    {
        var item = new CalendarItem { Category = ItemCategory.General };
        Assert.Null(CompletenessScorer.ScoreItem(item, calendarExempt: true));
    }

    [Fact]
    public void Presence_segment_scores_null()
    {
        var item = new CalendarItem { Details = new ItemDetails(Presence: new PresenceDetail(AvailabilityStatus.Office)) };
        Assert.Null(CompletenessScorer.ScoreItem(item, calendarExempt: false));
    }

    [Fact]
    public void Item_carrying_a_payload_scores_null()
    {
        var item = new CalendarItem { Category = ItemCategory.General, Prompt = SamplePrompt() };
        Assert.Null(CompletenessScorer.ScoreItem(item, calendarExempt: false));
    }

    [Fact]
    public void Empty_meeting_scores_zero_with_heaviest_gaps_first()
    {
        var item = new CalendarItem { Category = ItemCategory.Meeting };
        var score = CompletenessScorer.ScoreItem(item, false)!;

        Assert.Equal(0, score.Score);
        Assert.Equal(CompletenessScorer.Version, score.RubricVersion);
        // location(2) and attendees(2) outrank time(1)/description(1).
        Assert.Equal(["location", "attendees"], score.Gaps.Take(2).Select(g => g.Field));
        Assert.All(score.Gaps, g => Assert.Equal(GapSeverity.Absent, g.Severity));
    }

    [Fact]
    public void Fully_documented_meeting_scores_one()
    {
        var item = new CalendarItem
        {
            Category = ItemCategory.Meeting,
            PlaceId = Guid.NewGuid(),
            StartsAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            Title = "Sync",
            Description = "Quarterly planning agenda and pre-reads",
            Attendees = [new ItemAttendee { Status = ParticipationStatus.Accepted }],
        };
        var score = CompletenessScorer.ScoreItem(item, false)!;

        Assert.Equal(1, score.Score);
        Assert.Empty(score.Gaps);
    }

    [Fact]
    public void Description_echoing_the_title_and_unanswered_attendees_are_weak()
    {
        var item = new CalendarItem
        {
            Category = ItemCategory.Meeting,
            PlaceId = Guid.NewGuid(),
            StartsAt = DateTimeOffset.UtcNow,
            Title = "Standup",
            Description = "standup",
            Attendees = [new ItemAttendee { Status = ParticipationStatus.NeedsAction }],
        };
        var score = CompletenessScorer.ScoreItem(item, false)!;

        Assert.Equal(GapSeverity.Weak, score.Gaps.Single(g => g.Field == "description").Severity);
        Assert.Equal(GapSeverity.Weak, score.Gaps.Single(g => g.Field == "attendees").Severity);
        Assert.True(score.Score is > 0 and < 1);
    }

    [Fact]
    public void Trip_rubric_reads_travel_details()
    {
        var item = new CalendarItem
        {
            Category = ItemCategory.Trip,
            StartsAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
            Details = new ItemDetails(
                Booking: new BookingDetail(null, null, "BR-123", null, null, null, null),
                Travel: new TravelLeg(TransportMode.Flight, Guid.NewGuid(), Guid.NewGuid(), null, null, "SAS", "SK123", null, null, "14C", null)),
        };
        var score = CompletenessScorer.ScoreItem(item, false)!;

        Assert.True(score.Score > 0.9, $"expected near-complete trip, got {score.Score}");
        Assert.DoesNotContain(score.Gaps, g => g.Field == "carrier");
    }

    [Fact]
    public void Cancelled_item_scores_null()
    {
        var item = new CalendarItem { Category = ItemCategory.Meeting, Status = ItemStatus.Cancelled };
        Assert.Null(CompletenessScorer.ScoreItem(item, calendarExempt: false));
    }

    [Fact]
    public void Fuzzy_start_precision_scores_time_weak()
    {
        var item = new CalendarItem
        {
            Category = ItemCategory.General,
            StartsAt = new DateTimeOffset(2019, 6, 1, 0, 0, 0, TimeSpan.Zero),
            StartPrecision = DatePrecision.Month,
        };
        var score = CompletenessScorer.ScoreItem(item, false)!;

        Assert.Equal(GapSeverity.Weak, score.Gaps.Single(g => g.Field == "time").Severity);
    }

    [Fact]
    public void All_day_is_weak_time_for_clocked_categories_but_full_for_the_default_cut()
    {
        var meeting = new CalendarItem { Category = ItemCategory.Meeting, IsAllDay = true, StartDate = new DateOnly(2026, 7, 1) };
        var general = new CalendarItem { Category = ItemCategory.General, IsAllDay = true, StartDate = new DateOnly(2026, 7, 1) };

        Assert.Equal(GapSeverity.Weak, CompletenessScorer.ScoreItem(meeting, false)!.Gaps.Single(g => g.Field == "time").Severity);
        Assert.DoesNotContain(CompletenessScorer.ScoreItem(general, false)!.Gaps, g => g.Field == "time");
    }

    [Fact]
    public void Trip_leg_times_count_as_depart_arrive_times()
    {
        var item = new CalendarItem
        {
            Category = ItemCategory.Trip,
            IsAllDay = true,
            StartDate = new DateOnly(2026, 7, 1),
            Details = new ItemDetails(Travel: new TravelLeg(
                TransportMode.Flight, Guid.NewGuid(), Guid.NewGuid(),
                DepartAt: new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
                ArriveAt: new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
                null, null, null, null, null, null)),
        };
        var score = CompletenessScorer.ScoreItem(item, false)!;

        Assert.DoesNotContain(score.Gaps, g => g.Field == "departArriveTimes");
    }

    [Fact]
    public void All_day_stay_span_scores_check_in_out_weak_not_absent()
    {
        var item = new CalendarItem
        {
            Category = ItemCategory.Stay,
            IsAllDay = true,
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 4),
        };
        var score = CompletenessScorer.ScoreItem(item, false)!;

        Assert.Equal(GapSeverity.Weak, score.Gaps.Single(g => g.Field == "checkInOut").Severity);
    }

    [Fact]
    public void Parent_trip_scores_on_the_general_cut()
    {
        var item = new CalendarItem { Category = ItemCategory.Trip, PlaceId = Guid.NewGuid() };
        var score = CompletenessScorer.ScoreItem(item, calendarExempt: false, hasChildren: true)!;

        Assert.Equal(["time", "description"], score.Gaps.Select(g => g.Field));
        Assert.DoesNotContain(score.Gaps, g => g.Field is "carrier" or "seat" or "booking" or "fromToPlace");
    }

    [Fact]
    public void Partial_booking_is_weak_not_absent()
    {
        var item = new CalendarItem
        {
            Category = ItemCategory.Meal,
            Details = new ItemDetails(Booking: new BookingDetail(null, null, null, "https://book.example/r/42", null, null, null)),
        };
        var score = CompletenessScorer.ScoreItem(item, false)!;

        Assert.Equal(GapSeverity.Weak, score.Gaps.Single(g => g.Field == "booking").Severity);
    }

    [Theory]
    [InlineData(ItemCategory.Occasion)]
    [InlineData(ItemCategory.Meal)]
    [InlineData(ItemCategory.Outing)]
    public void Social_categories_score_attendees(ItemCategory category)
    {
        var item = new CalendarItem { Category = category };
        var score = CompletenessScorer.ScoreItem(item, false)!;

        Assert.Equal(GapSeverity.Absent, score.Gaps.Single(g => g.Field == "attendees").Severity);
    }

    [Fact]
    public void Gaps_rank_by_missing_mass_not_raw_weight()
    {
        // time is Weak (deficit 0.5), description Absent (deficit 1) — same weight, absent first.
        var item = new CalendarItem
        {
            Category = ItemCategory.Meeting,
            PlaceId = Guid.NewGuid(),
            StartsAt = new DateTimeOffset(2019, 6, 1, 0, 0, 0, TimeSpan.Zero),
            StartPrecision = DatePrecision.Month,
            Attendees = [new ItemAttendee { Status = ParticipationStatus.Accepted }],
        };
        var score = CompletenessScorer.ScoreItem(item, false)!;

        Assert.Equal(["description", "time"], score.Gaps.Select(g => g.Field));
    }
}
