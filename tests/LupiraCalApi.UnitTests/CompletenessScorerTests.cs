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
}
