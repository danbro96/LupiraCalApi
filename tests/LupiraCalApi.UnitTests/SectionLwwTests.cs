using LupiraCalApi.Domain;
using Xunit;
using static LupiraCalApi.UnitTests.TestEvents;

namespace LupiraCalApi.UnitTests;

/// <summary>The per-section LWW rules: the wins predicate itself, the sequence fallback for unstamped events,
/// and the aggregate-level guard behavior (stale replays lose, sections stay independent, order-independent
/// convergence, absorbing delete). The mobile client's reducer must produce identical outcomes — its test
/// vectors are emitted from these same rules by tools/FixtureEmitter.</summary>
public class SectionLwwTests
{
    static readonly DateTimeOffset T1 = new(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset T2 = new(2026, 7, 10, 11, 0, 0, TimeSpan.Zero);

    // ---- wins predicate ----

    [Fact]
    public void Later_occurredAt_wins_regardless_of_commandId()
    {
        Assert.True(SectionLww.Wins(T2, Guid.Empty, T1, Guid.NewGuid()));
        Assert.False(SectionLww.Wins(T1, Guid.NewGuid(), T2, Guid.Empty));
    }

    [Fact]
    public void Equal_occurredAt_breaks_ties_on_ordinal_commandId_string()
    {
        var lo = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var hi = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        Assert.True(SectionLww.Wins(T1, hi, T1, lo));
        Assert.False(SectionLww.Wins(T1, lo, T1, hi));
    }

    [Fact]
    public void Equal_pair_is_a_replay_and_loses()
    {
        var cmd = Guid.NewGuid();
        Assert.False(SectionLww.Wins(T1, cmd, T1, cmd));
    }

    [Fact]
    public void Sequence_fallback_ids_order_like_their_sequences()
    {
        // Zero-padded hex: the canonical GUID string of a later sequence always compares greater —
        // unstamped events keep append order even across the 9→10 and 15→16 digit boundaries.
        long[] seqs = [1, 9, 10, 15, 16, 255, 4095, 1_000_000];
        for (var i = 1; i < seqs.Length; i++)
            Assert.True(SectionLww.CompareCommandId(SectionLww.FromSequence(seqs[i]), SectionLww.FromSequence(seqs[i - 1])) > 0);
    }

    // ---- aggregate guards ----

    static CalendarItemFields Fields(string title) => new(
        title, null, ItemStatus.Confirmed, false,
        new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
        "UTC", null, null, null, null, null, null, ItemCategory.General, null, null, null, null);

    static CalendarItem Scheduled(Guid id)
    {
        var i = new CalendarItem();
        i.Apply(Ev(new ItemScheduled(id, $"{id:N}@x", Fields("Original"), null)));
        return i;
    }

    [Fact]
    public void Core_revisions_converge_regardless_of_arrival_order()
    {
        var id = Guid.NewGuid();
        var older = new ItemRevised(id, Fields("Older"), null, T1, Guid.NewGuid());
        var newer = new ItemRevised(id, Fields("Newer"), null, T2, Guid.NewGuid());

        var inOrder = Scheduled(id);
        inOrder.Apply(Ev(older));
        inOrder.Apply(Ev(newer));

        var outOfOrder = Scheduled(id);
        outOfOrder.Apply(Ev(newer));
        outOfOrder.Apply(Ev(older));   // stale replay arrives late — must lose

        Assert.Equal("Newer", inOrder.Title);
        Assert.Equal("Newer", outOfOrder.Title);
        Assert.Equal(inOrder.CoreTs, outOfOrder.CoreTs);
        Assert.Equal(inOrder.CoreCmd, outOfOrder.CoreCmd);
    }

    [Fact]
    public void Stale_core_edit_predating_creation_loses_to_the_create_seed()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);   // create seeds the core guard from the server stamp
        var preCreate = new ItemRevised(id, Fields("Stale"), null, i.CoreTs.AddMinutes(-5), Guid.NewGuid());
        i.Apply(Ev(preCreate));
        Assert.Equal("Original", i.Title);
    }

    [Fact]
    public void Metadata_guard_is_independent_of_core()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);
        var baseTs = i.UpdatedAt;
        i.Apply(Ev(new ItemMetadataAttached(id, """{"k":"new"}""", baseTs.AddHours(2), Guid.NewGuid())));
        // A newer core edit must not let a stale metadata blob through.
        i.Apply(Ev(new ItemRevised(id, Fields("Newer core"), null, baseTs.AddHours(3), Guid.NewGuid())));
        i.Apply(Ev(new ItemMetadataAttached(id, """{"k":"stale"}""", baseTs.AddHours(1), Guid.NewGuid())));

        Assert.Equal("Newer core", i.Title);
        Assert.Equal("""{"k":"new"}""", i.Metadata);
    }

    [Fact]
    public void Stale_action_loses_to_newer_prompt_on_the_shared_payload_guard()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);
        var baseTs = i.UpdatedAt;
        var prompt = new ItemPrompt(PromptIntent.Summarise, null, "do", OutputKind.Summary, null, null, FallbackMode.Retry, new PromptFire(PromptFireKind.OnStart, null, null), true);
        var action = new ItemAction(ActionKind.Notify, null, "{}", new PromptFire(PromptFireKind.OnStart, null, null), true);

        i.Apply(Ev(new ItemPromptSet(id, prompt, baseTs.AddHours(2), Guid.NewGuid())));
        i.Apply(Ev(new ItemActionSet(id, action, baseTs.AddHours(1), Guid.NewGuid())));   // stale — must not clobber the XOR slot

        Assert.NotNull(i.Prompt);
        Assert.Null(i.Action);
    }

    [Fact]
    public void Filing_guards_are_per_calendar()
    {
        var id = Guid.NewGuid();
        var calA = Guid.NewGuid();
        var calB = Guid.NewGuid();
        var i = Scheduled(id);
        var baseTs = i.UpdatedAt;

        i.Apply(Ev(new AddedToCalendar(id, calA, CalendarEntryStatus.Accepted, baseTs.AddHours(2), Guid.NewGuid())));
        i.Apply(Ev(new RemovedFromCalendar(id, calA, baseTs.AddHours(1), Guid.NewGuid())));   // stale unfile of A loses
        i.Apply(Ev(new AddedToCalendar(id, calB, CalendarEntryStatus.Accepted, baseTs.AddHours(1), Guid.NewGuid())));   // B is untouched by A's guard

        Assert.True(i.IsAcceptedIn(calA));
        Assert.True(i.IsAcceptedIn(calB));
    }

    [Fact]
    public void Delete_absorbs_later_section_writes()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);
        i.Apply(Ev(new ItemDeleted(id, T2)));
        i.Apply(Ev(new ItemRevised(id, Fields("After delete"), null, T2.AddHours(1), Guid.NewGuid())));

        Assert.NotNull(i.DeletedAt);
        Assert.Equal("Original", i.Title);
    }

    [Fact]
    public void Touch_tracks_created_updated_and_watermark()
    {
        var id = Guid.NewGuid();
        var i = new CalendarItem();
        var create = Ev(new ItemScheduled(id, $"{id:N}@x", Fields("Original"), null));
        i.Apply(create);
        var revise = Ev(new ItemRevised(id, Fields("Two"), null));
        i.Apply(revise);

        Assert.Equal(create.Timestamp, i.CreatedAt);
        Assert.Equal(revise.Timestamp, i.UpdatedAt);
        Assert.Equal(revise.Sequence, i.UpdatedSequence);
    }

    [Fact]
    public void Unstamped_events_apply_in_append_order()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);
        i.Apply(Ev(new ItemRevised(id, Fields("First"), null)));
        i.Apply(Ev(new ItemRevised(id, Fields("Second"), null)));
        Assert.Equal("Second", i.Title);
    }
}
