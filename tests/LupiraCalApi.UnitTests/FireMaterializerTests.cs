using LupiraCalApi.Domain;
using LupiraCalApi.Scheduling;
using LupiraCalApi.Serialization;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class FireMaterializerTests
{
    static readonly DateTimeOffset Now = new(2026, 6, 25, 0, 0, 0, TimeSpan.Zero);
    static readonly TimeSpan Horizon = TimeSpan.FromDays(35);
    static readonly IFireMaterializer Mat = new FireMaterializer(new RecurrenceExpander());

    static CalendarItem Timed(PromptFire fire, bool enabled = true, ItemPrompt? prompt = null, ItemAction? action = null)
    {
        var item = new CalendarItem
        {
            Id = Guid.NewGuid(),
            StartsAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            StartTimezone = "UTC",
            Calendars = [new CalendarMembership { CalendarId = Guid.NewGuid(), Status = CalendarEntryStatus.Accepted }],
        };
        if (action is not null) item.Action = action with { Fire = fire, Enabled = enabled };
        else item.Prompt = (prompt ?? Prompt(fire)) with { Fire = fire, Enabled = enabled };
        return item;
    }

    static ItemPrompt Prompt(PromptFire fire) => new(PromptIntent.Monitor, null, "x", OutputKind.Summary, null, null, FallbackMode.Retry, fire, true);
    static PromptFire OnStart => new(PromptFireKind.OnStart, null, null);

    [Fact]
    public void No_payload_yields_no_rows()
    {
        var item = new CalendarItem { Id = Guid.NewGuid(), StartsAt = Now };
        Assert.Empty(Mat.Materialize(item, null, Now, Horizon));
    }

    [Fact]
    public void Disabled_payload_yields_no_rows()
    {
        var item = Timed(OnStart, enabled: false);
        Assert.Empty(Mat.Materialize(item, null, Now, Horizon));
    }

    [Fact]
    public void OnStart_fires_at_the_start()
    {
        var item = Timed(OnStart);
        var row = Assert.Single(Mat.Materialize(item, null, Now, Horizon));
        Assert.Equal(item.StartsAt, row.OccurrenceAt);
        Assert.Equal(TimeSpan.FromHours(24), row.ExpireAfter);   // no calendar kind → fallback
        Assert.StartsWith(item.Id.ToString("N"), row.DedupeKey);
    }

    [Fact]
    public void OnEnd_fires_at_the_end()
    {
        var item = Timed(new PromptFire(PromptFireKind.OnEnd, null, null));
        var row = Assert.Single(Mat.Materialize(item, null, Now, Horizon));
        Assert.Equal(item.EndsAt, row.OccurrenceAt);
    }

    [Fact]
    public void Offset_is_a_lead_time_with_a_short_expiry()
    {
        var item = Timed(new PromptFire(PromptFireKind.Offset, -30, null));
        var row = Assert.Single(Mat.Materialize(item, null, Now, Horizon));
        Assert.Equal(item.StartsAt!.Value.AddMinutes(-30), row.OccurrenceAt);
        Assert.Equal(TimeSpan.FromMinutes(30), row.ExpireAfter);   // leave-by / reminder
    }

    [Fact]
    public void AllDayAt_fires_at_the_local_time_on_the_occurrence_date()
    {
        var item = new CalendarItem
        {
            Id = Guid.NewGuid(), IsAllDay = true,
            StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 7, 2), StartTimezone = "UTC",
            Calendars = [new CalendarMembership { CalendarId = Guid.NewGuid(), Status = CalendarEntryStatus.Accepted }],
            Prompt = Prompt(new PromptFire(PromptFireKind.AllDayAt, null, new TimeOnly(9, 0))),
        };
        var row = Assert.Single(Mat.Materialize(item, null, Now, Horizon));
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero), row.OccurrenceAt);
    }

    [Fact]
    public void Expire_after_keys_off_the_calendar_kind()
    {
        var item = Timed(OnStart);
        Assert.Equal(TimeSpan.FromHours(6), Mat.Materialize(item, CalendarKind.LlmPrompts, Now, Horizon).Single().ExpireAfter);
        Assert.Equal(TimeSpan.FromDays(3), Mat.Materialize(item, CalendarKind.DevOps, Now, Horizon).Single().ExpireAfter);
    }

    [Fact]
    public void Action_payload_materializes_too()
    {
        var item = Timed(new PromptFire(PromptFireKind.OnEnd, null, null),
            action: new ItemAction(ActionKind.SendCheckIn, null, "{}", new PromptFire(PromptFireKind.OnEnd, null, null), true));
        var row = Assert.Single(Mat.Materialize(item, null, Now, Horizon));
        Assert.Equal(item.EndsAt, row.OccurrenceAt);
    }

    [Fact]
    public void Recurring_payload_expands_over_the_horizon_with_distinct_dedupe_keys()
    {
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var item = new CalendarItem
        {
            Id = Guid.NewGuid(),
            StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC", RecurrenceRule = "FREQ=WEEKLY",
            Calendars = [new CalendarMembership { CalendarId = Guid.NewGuid(), Status = CalendarEntryStatus.Accepted }],
            Prompt = Prompt(OnStart),
        };
        var rows = Mat.Materialize(item, null, start, Horizon);

        Assert.True(rows.Count >= 4, $"expected >=4 weekly fires in 35d, got {rows.Count}");
        Assert.Equal(rows.Count, rows.Select(r => r.DedupeKey).Distinct().Count());
    }

    [Fact]
    public void Materialize_is_idempotent_in_row_identity()
    {
        var item = Timed(OnStart);
        var first = Mat.Materialize(item, null, Now, Horizon).Single();
        var second = Mat.Materialize(item, null, Now, Horizon).Single();
        Assert.Equal(first.Id, second.Id);             // deterministic id from dedupe_key → on-conflict-do-nothing safe
        Assert.Equal(first.DedupeKey, second.DedupeKey);
    }
}
