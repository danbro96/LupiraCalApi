using Lupira.Results;
using LupiraCalApi.Core.Abstractions;
using LupiraCalApi.Core.Auth;
using LupiraCalApi.Core.Data;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.CalendarItems.Events;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Mappers;
using Marten;

namespace LupiraCalApi.Core.Application.Items;

/// <summary>First-class participation: invited / responded / attended / left, appended to the item's stream. The
/// embedded <see cref="ItemAttendee"/> read model composes the timestamps. Every attendee is a LupiraContactApi
/// contact, referenced by bare Guid and validated via <see cref="IContactResolver"/> when configured.
/// <para>Commands are replay-safe for offline clients: an <c>Idempotency-Key</c> makes a redelivery return the current
/// item (see <see cref="Idempotency"/>), and a command that re-asserts what the item already says appends nothing.
/// <c>occurredAt</c> stamps the event with when the client acted.</para></summary>
public sealed class ParticipationService(
    IDocumentSession session, AccessResolver access, CompletenessResolver completeness, IContactResolver contacts, Idempotency idempotency,
    RecurrenceExpander expander, TimeProvider clock)
{
    private static readonly TimeSpan HalfLife = TimeSpan.FromDays(90);

    // At three half-life-years an occurrence weighs ~0.02%: past that it can't move a ranking, so it isn't expanded.
    private static readonly TimeSpan ScoreLookback = TimeSpan.FromDays(3 * 365);
    private static readonly TimeSpan NextLookahead = TimeSpan.FromDays(366);

    public async Task<OpResult<CalendarItemDto>> InviteAsync(
        Guid principalId, Guid itemId, Guid contactId, string? role, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default)
    {
        var parsedRole = ParticipationRole.RequiredParticipant;
        if (!string.IsNullOrWhiteSpace(role) && !ParticipationTokens.TryParseRole(role, out parsedRole))
            return OpResult<CalendarItemDto>.Invalid($"Unknown role '{role}'. Valid values: {ParticipationTokens.RoleTokens}.");

        // Fail-open: a null result means resolution is unavailable (unconfigured/transport) — proceed as before.
        // A non-null result missing the id is a definitive "no such contact".
        if (contacts.IsConfigured
            && await contacts.ResolveAsync([contactId], ct) is { } resolved
            && resolved.All(c => c.ContactId != contactId))
            return OpResult<CalendarItemDto>.Invalid("Unknown contact.");

        return await MutateAsync(principalId, itemId, commandId, item => Events(item.Attendees.Any(a => a.ContactId == contactId)
            ? null
            : new AttendeeInvited(itemId, Guid.NewGuid(), contactId, parsedRole, At(occurredAt))), ct);
    }

    public Task<OpResult<CalendarItemDto>> RespondAsync(
        Guid principalId, Guid itemId, Guid participationId, string? status, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default) =>
        ParticipationTokens.TryParseStatus(status, out var parsed)
            ? MutateAttendeeAsync(principalId, itemId, participationId, commandId, a => a.Status == parsed
                ? null
                : new InvitationResponded(itemId, participationId, parsed, At(occurredAt)), ct)
            : Task.FromResult(OpResult<CalendarItemDto>.Invalid($"Unknown status '{status}'. Valid values: {ParticipationTokens.StatusTokens}."));

    public Task<OpResult<CalendarItemDto>> ConfirmAttendanceAsync(
        Guid principalId, Guid itemId, Guid participationId, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default) =>
        MutateAttendeeAsync(principalId, itemId, participationId, commandId, a => a.AttendedAt is null
            ? new AttendanceConfirmed(itemId, participationId, At(occurredAt))
            : null, ct);

    public Task<OpResult<CalendarItemDto>> MarkLeftAsync(
        Guid principalId, Guid itemId, Guid participationId, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default) =>
        MutateAttendeeAsync(principalId, itemId, participationId, commandId, a => a.LeftAt is null
            ? new ParticipantLeft(itemId, participationId, At(occurredAt))
            : null, ct);

    public Task<OpResult<CalendarItemDto>> RemoveAsync(Guid principalId, Guid itemId, Guid participationId, Guid? commandId = null, CancellationToken ct = default) =>
        MutateAttendeeAsync(principalId, itemId, participationId, commandId, a => new AttendeeRemoved(itemId, a.ParticipationId), ct);

    /// <summary>The counterpart of the contact-keyed invite, for clients that never learned the participation id (an
    /// invite still in an offline outbox): every row the contact holds goes, and a contact holding none is already done.</summary>
    public Task<OpResult<CalendarItemDto>> RemoveContactAsync(Guid principalId, Guid itemId, Guid contactId, Guid? commandId = null, CancellationToken ct = default) =>
        MutateAsync(principalId, itemId, commandId, item => OpResult<IReadOnlyList<object>>.Ok(
            [.. item.Attendees.Where(a => a.ContactId == contactId).Select(a => new AttendeeRemoved(itemId, a.ParticipationId))]), ct);

    public const int MaxAttendees = 200;

    /// <summary>Add a set of contacts as attendees in one call (add-only — existing attendees are kept). When
    /// <paramref name="attended"/>, each is also marked attended (historical backfill) via <see cref="AttendanceConfirmed"/>.
    /// Returns a slim result (the additions + already-present count), not the full item DTO.</summary>
    public async Task<OpResult<SetParticipantsResult>> SetParticipantsAsync(Guid principalId, Guid itemId, IReadOnlyList<Guid> contactIds, bool attended, CancellationToken ct = default)
    {
        var distinct = contactIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0) return OpResult<SetParticipantsResult>.Invalid("At least one contactId is required.");
        if (distinct.Count > MaxAttendees) return OpResult<SetParticipantsResult>.Invalid($"At most {MaxAttendees} attendees per call.");

        // Validate contacts (fail-open when unconfigured/unreachable, matching InviteAsync).
        if (contacts.IsConfigured && await contacts.ResolveAsync(distinct, ct) is { } resolved)
        {
            var known = resolved.Select(c => c.ContactId).ToHashSet();
            var unknown = distinct.Where(id => !known.Contains(id)).ToList();
            if (unknown.Count > 0) return OpResult<SetParticipantsResult>.Invalid($"Unknown contact(s): {string.Join(", ", unknown)}.");
        }

        var stream = await session.Events.FetchForWriting<CalendarItem>(itemId, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<SetParticipantsResult>.NotFound();
        if (!await access.CanWriteItemAsync(principalId, item, ct)) return OpResult<SetParticipantsResult>.Forbidden("No write access to this item.");

        // A contact should hold one participation, but legacy rows may repeat one; the first stands for all.
        var existing = item.Attendees.GroupBy(a => a.ContactId).ToDictionary(g => g.Key, g => g.First());
        var added = new List<ParticipationRef>();
        var now = DateTimeOffset.UtcNow;
        var alreadyPresent = 0;
        var appended = false;
        foreach (var cid in distinct)
        {
            if (existing.TryGetValue(cid, out var a))
            {
                alreadyPresent++;
                if (attended && a.AttendedAt is null)
                {
                    stream.AppendOne(new AttendanceConfirmed(itemId, a.ParticipationId, now));
                    appended = true;
                }

                continue;
            }

            var pid = Guid.NewGuid();
            stream.AppendOne(new AttendeeInvited(itemId, pid, cid, ParticipationRole.RequiredParticipant, now));
            if (attended) stream.AppendOne(new AttendanceConfirmed(itemId, pid, now));
            appended = true;
            added.Add(new ParticipationRef(cid, pid));
        }

        if (appended) await session.SaveChangesAsync(ct);
        return OpResult<SetParticipantsResult>.Ok(new SetParticipantsResult(itemId, added, alreadyPresent));
    }

    /// <summary>Per-contact participation across the caller's readable calendars, ordered by recency-weighted
    /// <see cref="ParticipationSummaryEntry.Score"/>. Optional from/to restricts to items whose occurrence start falls in
    /// the window (start-less items only match the unbounded query).</summary>
    public async Task<OpResult<List<ParticipationSummaryEntry>>> SummaryAsync(Guid principalId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        var calIds = await access.AccessibleCalendarIdsAsync(principalId, ct);
        var items = await session.Query<CalendarItem>().Where(i => i.DeletedAt == null).ToListAsync(ct);
        return OpResult<List<ParticipationSummaryEntry>>.Ok(Summarize(items, calIds, from, to, clock.GetUtcNow(), expander));
    }

    internal static List<ParticipationSummaryEntry> Summarize(
        IEnumerable<CalendarItem> items, IReadOnlyCollection<Guid> readableCalendarIds, DateTimeOffset? from, DateTimeOffset? to,
        DateTimeOffset now, RecurrenceExpander expander)
    {
        var perContact = new Dictionary<Guid, (int Count, DateTimeOffset? LastAt, double Score)>();
        foreach (var i in items)
        {
            if (!i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && readableCalendarIds.Contains(m.CalendarId))) continue;
            var at = CalendarItemService.OccurrenceStart(i);
            if (from is { } f && (at is null || at < f)) continue;
            if (to is { } t && (at is null || at >= t)) continue;
            // Withdrawn participations (LeftAt) don't count as interaction; removed attendees are already gone.
            var attendees = i.Attendees.Where(a => a.LeftAt is null).DistinctBy(a => a.ContactId).ToList();
            if (attendees.Count == 0) continue;
            var weight = at is null ? 0 : RecencyWeight(i, now, from, to, expander);
            foreach (var a in attendees)
            {
                var prev = perContact.GetValueOrDefault(a.ContactId);
                perContact[a.ContactId] = (prev.Count + 1, at > prev.LastAt || prev.LastAt is null ? at : prev.LastAt, prev.Score + weight);
            }
        }

        return [.. perContact
            .Select(kv => new ParticipationSummaryEntry(kv.Key, kv.Value.Count, kv.Value.LastAt, Math.Round(kv.Value.Score, 4)))
            .OrderByDescending(e => e.Score).ThenByDescending(e => e.Count)
            .ThenByDescending(e => e.LastAt ?? DateTimeOffset.MinValue).ThenBy(e => e.ContactId)];
    }

    /// <summary>Each past occurrence weighs 0.5^(age / 90 days), so a weekly series outranks a one-off and people you
    /// stopped meeting fade. The next planned occurrence weighs 1 — a meeting on the books is a current relationship —
    /// and only the next, so an open-ended series doesn't count forever.</summary>
    internal static double RecencyWeight(CalendarItem item, DateTimeOffset now, DateTimeOffset? from, DateTimeOffset? to, RecurrenceExpander expander)
    {
        var past = expander.Expand(item, Later(now - ScoreLookback, from), Earlier(now, to))
            .Sum(s => Math.Pow(0.5, (now - s) / HalfLife));
        var planned = expander.Expand(item, Later(now, from), Earlier(now + NextLookahead, to)).Count > 0 ? 1 : 0;
        return past + planned;
    }

    private static DateTimeOffset Later(DateTimeOffset a, DateTimeOffset? b) => b is { } v && v > a ? v : a;

    private static DateTimeOffset Earlier(DateTimeOffset a, DateTimeOffset? b) => b is { } v && v < a ? v : a;

    /// <summary>Runs one command against the item. <paramref name="decide"/> returns the events to append — none when
    /// the item already says so — or the reason the command can't apply.</summary>
    private async Task<OpResult<CalendarItemDto>> MutateAsync(
        Guid principalId, Guid itemId, Guid? commandId, Func<CalendarItem, OpResult<IReadOnlyList<object>>> decide, CancellationToken ct)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await CurrentAsync(itemId, ct);
        var stream = await session.Events.FetchForWriting<CalendarItem>(itemId, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        if (!await access.CanWriteItemAsync(principalId, item, ct)) return OpResult<CalendarItemDto>.Forbidden("No write access to this item.");

        var decision = decide(item);
        if (!decision.IsOk) return new OpResult<CalendarItemDto>(decision.Status, null, decision.Error);
        if (decision.Value is { Count: > 0 } events)
        {
            stream.AppendMany(events);
            await idempotency.CommitAsync(commandId, itemId, (int) (stream.CurrentVersion ?? 0) + events.Count, ct);
        }

        return await CurrentAsync(itemId, ct);
    }

    /// <summary>A command on one participation; an id the item doesn't hold is not found.</summary>
    private Task<OpResult<CalendarItemDto>> MutateAttendeeAsync(
        Guid principalId, Guid itemId, Guid participationId, Guid? commandId, Func<ItemAttendee, object?> decide, CancellationToken ct) =>
        MutateAsync(principalId, itemId, commandId, item => item.Attendees.FirstOrDefault(a => a.ParticipationId == participationId) is { } attendee
            ? Events(decide(attendee))
            : OpResult<IReadOnlyList<object>>.NotFound(), ct);

    private async Task<OpResult<CalendarItemDto>> CurrentAsync(Guid itemId, CancellationToken ct) =>
        await session.LoadAsync<CalendarItem>(itemId, ct) is { DeletedAt: null } item
            ? OpResult<CalendarItemDto>.Ok(item.ToResponse(await completeness.ScoreItemAsync(item, ct)))
            : OpResult<CalendarItemDto>.NotFound();

    private static OpResult<IReadOnlyList<object>> Events(object? single) => OpResult<IReadOnlyList<object>>.Ok(single is null ? [] : [single]);

    private static DateTimeOffset At(DateTimeOffset? occurredAt) => occurredAt?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
}
