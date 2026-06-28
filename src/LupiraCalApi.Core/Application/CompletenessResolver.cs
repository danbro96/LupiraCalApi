using LupiraCalApi.Domain;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>Resolves the derived completeness score for items and contacts. It lives outside the snapshot because
/// item exemption needs the item's calendar kinds, and a contact's organisation/role lives on a separate
/// <see cref="ContactGroup"/> — neither is visible to a single-stream snapshot.</summary>
public sealed class CompletenessResolver(IQuerySession session)
{
    public async Task<CompletenessScore?> ScoreItemAsync(CalendarItem item, CancellationToken ct = default)
    {
        var exempt = await ExemptCalendarIdsAsync([.. AcceptedIds(item)], ct);
        return CompletenessScorer.ScoreItem(item, AcceptedIds(item).Any(exempt.Contains));
    }

    public async Task<Dictionary<Guid, CompletenessScore?>> ScoreItemsAsync(IReadOnlyCollection<CalendarItem> items, CancellationToken ct = default)
    {
        var exempt = await ExemptCalendarIdsAsync([.. items.SelectMany(AcceptedIds).Distinct()], ct);
        return items.ToDictionary(i => i.Id, i => CompletenessScorer.ScoreItem(i, AcceptedIds(i).Any(exempt.Contains)));
    }

    public async Task<CompletenessScore?> ScoreContactAsync(Contact c, CancellationToken ct = default)
    {
        var orgMembers = await OrganisationMemberIdsAsync([c.Id], ct);
        return CompletenessScorer.ScoreContact(c, orgMembers.Contains(c.Id));
    }

    public async Task<Dictionary<Guid, CompletenessScore?>> ScoreContactsAsync(IReadOnlyCollection<Contact> contacts, CancellationToken ct = default)
    {
        var orgMembers = await OrganisationMemberIdsAsync([.. contacts.Select(c => c.Id)], ct);
        return contacts.ToDictionary(c => c.Id, c => CompletenessScorer.ScoreContact(c, orgMembers.Contains(c.Id)));
    }

    private static IEnumerable<Guid> AcceptedIds(CalendarItem i) =>
        i.Calendars.Where(m => m.Status == CalendarEntryStatus.Accepted).Select(m => m.CalendarId);

    // System calendars + the two special agenda kinds (Birthdays/Availability) are never check-in targets.
    private async Task<HashSet<Guid>> ExemptCalendarIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var cals = await session.Query<Calendar>().Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        return [.. cals.Where(c => c.Class == CalendarClass.System || c.Kind is CalendarKind.Birthdays or CalendarKind.Availability).Select(c => c.Id)];
    }

    private async Task<HashSet<Guid>> OrganisationMemberIdsAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct)
    {
        if (contactIds.Count == 0) return [];
        var idSet = contactIds.ToHashSet();
        var groups = await session.Query<ContactGroup>().Where(g => g.Kind == ContactGroupKind.Organization && g.DeletedAt == null).ToListAsync(ct);
        return [.. groups.SelectMany(g => g.MemberContactIds).Where(idSet.Contains)];
    }
}
