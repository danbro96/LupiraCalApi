using LupiraCalApi.Domain;
using Marten;

namespace LupiraCalApi.Auth;

/// <summary>Container-scoped authorization over the multi-owner membership docs: a principal may read a collection it
/// has any grant on, and write one it owns or has a read-write grant on.</summary>
public sealed class AccessResolver(IQuerySession session)
{
    public async Task<List<Guid>> AccessibleCalendarIdsAsync(Guid principalId, CancellationToken ct = default) =>
        await session.Query<CalendarOwner>().Where(o => o.PrincipalId == principalId).Select(o => o.CalendarId).ToListAsync(ct) is { } l ? [.. l] : [];

    public async Task<List<Guid>> AccessibleAddressBookIdsAsync(Guid principalId, CancellationToken ct = default) =>
        await session.Query<AddressBookOwner>().Where(o => o.PrincipalId == principalId).Select(o => o.AddressBookId).ToListAsync(ct) is { } l ? [.. l] : [];

    public async Task<bool> CanReadCalendarAsync(Guid principalId, Guid calendarId, CancellationToken ct = default) =>
        await session.Query<CalendarOwner>().AnyAsync(o => o.CalendarId == calendarId && o.PrincipalId == principalId, ct);

    public async Task<bool> CanWriteCalendarAsync(Guid principalId, Guid calendarId, CancellationToken ct = default) =>
        await session.Query<CalendarOwner>().AnyAsync(
            o => o.CalendarId == calendarId && o.PrincipalId == principalId && (o.Access == Access.Owner || o.Access == Access.ReadWrite), ct);

    public async Task<bool> CanReadAddressBookAsync(Guid principalId, Guid addressBookId, CancellationToken ct = default) =>
        await session.Query<AddressBookOwner>().AnyAsync(o => o.AddressBookId == addressBookId && o.PrincipalId == principalId, ct);

    public async Task<bool> CanWriteAddressBookAsync(Guid principalId, Guid addressBookId, CancellationToken ct = default) =>
        await session.Query<AddressBookOwner>().AnyAsync(
            o => o.AddressBookId == addressBookId && o.PrincipalId == principalId && (o.Access == Access.Owner || o.Access == Access.ReadWrite), ct);

    /// <summary>An item is readable/writable if the principal can read/write any calendar it is accepted into.</summary>
    public async Task<bool> CanReadItemAsync(Guid principalId, CalendarItem item, CancellationToken ct = default)
    {
        foreach (var m in item.Calendars.Where(x => x.Status == CalendarEntryStatus.Accepted))
            if (await CanReadCalendarAsync(principalId, m.CalendarId, ct)) return true;
        return false;
    }

    public async Task<bool> CanWriteItemAsync(Guid principalId, CalendarItem item, CancellationToken ct = default)
    {
        foreach (var m in item.Calendars.Where(x => x.Status == CalendarEntryStatus.Accepted))
            if (await CanWriteCalendarAsync(principalId, m.CalendarId, ct)) return true;
        return false;
    }
}
