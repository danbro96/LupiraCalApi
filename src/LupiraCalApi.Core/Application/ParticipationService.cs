using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Mappers;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>First-class participation: invited / responded / attended / left, appended to the item's stream. The
/// embedded <see cref="ItemAttendee"/> read model composes the timestamps. Every attendee is a LupiraContactApi
/// contact, referenced by bare Guid and validated via <see cref="IContactResolver"/> when configured.</summary>
public sealed class ParticipationService(IDocumentSession session, AccessResolver access, CompletenessResolver completeness, IContactResolver contacts)
{
    public async Task<OpResult<CalendarItemDto>> InviteAsync(Guid principalId, Guid itemId, Guid contactId, string? role, CancellationToken ct = default)
    {
        // Fail-open: a null result means resolution is unavailable (unconfigured/transport) — proceed as before.
        // A non-null result missing the id is a definitive "no such contact".
        if (contacts.IsConfigured
            && await contacts.ResolveAsync([contactId], ct) is { } resolved
            && resolved.All(c => c.ContactId != contactId))
            return OpResult<CalendarItemDto>.Invalid("Unknown contact.");

        // Idempotent by contact: a contact already holding a (non-removed) participation row is not re-invited.
        return await AppendAsync(principalId, itemId, item => item.Attendees.Any(a => a.ContactId == contactId)
            ? null
            : new AttendeeInvited(itemId, Guid.NewGuid(), contactId, ParseRole(role), DateTimeOffset.UtcNow), ct);
    }

    public Task<OpResult<CalendarItemDto>> RespondAsync(Guid principalId, Guid itemId, Guid participationId, string? status, CancellationToken ct = default) =>
        AppendAsync(principalId, itemId, _ => new InvitationResponded(itemId, participationId, ParseStat(status), DateTimeOffset.UtcNow), ct);

    public Task<OpResult<CalendarItemDto>> ConfirmAttendanceAsync(Guid principalId, Guid itemId, Guid participationId, CancellationToken ct = default) =>
        AppendAsync(principalId, itemId, _ => new AttendanceConfirmed(itemId, participationId, DateTimeOffset.UtcNow), ct);

    public Task<OpResult<CalendarItemDto>> MarkLeftAsync(Guid principalId, Guid itemId, Guid participationId, CancellationToken ct = default) =>
        AppendAsync(principalId, itemId, _ => new ParticipantLeft(itemId, participationId, DateTimeOffset.UtcNow), ct);

    public Task<OpResult<CalendarItemDto>> RemoveAsync(Guid principalId, Guid itemId, Guid participationId, CancellationToken ct = default) =>
        AppendAsync(principalId, itemId, _ => new AttendeeRemoved(itemId, participationId), ct);

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

        var existing = item.Attendees.ToDictionary(a => a.ContactId);
        var added = new List<ParticipationRef>();
        var now = DateTimeOffset.UtcNow;
        var alreadyPresent = 0;
        var appended = false;
        foreach (var cid in distinct)
        {
            if (existing.TryGetValue(cid, out var a))
            {
                alreadyPresent++;
                if (attended && a.AttendedAt is null) { stream.AppendOne(new AttendanceConfirmed(itemId, a.ParticipationId, now)); appended = true; }
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

    /// <summary>Per-contact participation across the caller's readable calendars, ordered most-interacted first.
    /// Optional from/to restricts to items whose occurrence start falls in the window (start-less items only match
    /// the unbounded query).</summary>
    public async Task<OpResult<List<ParticipationSummaryEntry>>> SummaryAsync(Guid principalId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        var calIds = await access.AccessibleCalendarIdsAsync(principalId, ct);
        var items = await session.Query<CalendarItem>().Where(i => i.DeletedAt == null).ToListAsync(ct);
        return OpResult<List<ParticipationSummaryEntry>>.Ok(Summarize(items, calIds, from, to));
    }

    internal static List<ParticipationSummaryEntry> Summarize(IEnumerable<CalendarItem> items, IReadOnlyCollection<Guid> readableCalendarIds, DateTimeOffset? from, DateTimeOffset? to)
    {
        var perContact = new Dictionary<Guid, (int Count, DateTimeOffset? LastAt)>();
        foreach (var i in items)
        {
            if (!i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && readableCalendarIds.Contains(m.CalendarId))) continue;
            var at = CalendarItemService.OccurrenceStart(i);
            if (from is { } f && (at is null || at < f)) continue;
            if (to is { } t && (at is null || at >= t)) continue;
            // Withdrawn participations (LeftAt) don't count as interaction; removed attendees are already gone.
            foreach (var a in i.Attendees.Where(a => a.LeftAt is null).DistinctBy(a => a.ContactId))
            {
                var prev = perContact.GetValueOrDefault(a.ContactId);
                perContact[a.ContactId] = (prev.Count + 1, at > prev.LastAt || prev.LastAt is null ? at : prev.LastAt);
            }
        }
        return [.. perContact
            .Select(kv => new ParticipationSummaryEntry(kv.Key, kv.Value.Count, kv.Value.LastAt))
            .OrderByDescending(e => e.Count).ThenByDescending(e => e.LastAt ?? DateTimeOffset.MinValue).ThenBy(e => e.ContactId)];
    }

    private async Task<OpResult<CalendarItemDto>> AppendAsync(Guid principalId, Guid itemId, Func<CalendarItem, object?> makeEvent, CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<CalendarItem>(itemId, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        if (!await access.CanWriteItemAsync(principalId, item, ct)) return OpResult<CalendarItemDto>.Forbidden("No write access to this item.");
        if (makeEvent(item) is { } evt)
        {
            stream.AppendOne(evt);
            await session.SaveChangesAsync(ct);
        }
        var updated = await session.LoadAsync<CalendarItem>(itemId, ct);
        return OpResult<CalendarItemDto>.Ok(updated!.ToResponse(await completeness.ScoreItemAsync(updated!, ct)));
    }

    private static ParticipationRole ParseRole(string? s) => Enum.TryParse<ParticipationRole>(s, true, out var v) ? v : ParticipationRole.RequiredParticipant;
    private static ParticipationStatus ParseStat(string? s) => Enum.TryParse<ParticipationStatus>(s, true, out var v) ? v : ParticipationStatus.NeedsAction;
}
