using LupiraCalApi.Data;
using LupiraCalApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LupiraCalApi.Auth;

/// <summary>Container-scoped authorization: a user may touch a calendar/address book they own OR are shared into.</summary>
public sealed class AccessResolver(CalDbContext db)
{
    public IQueryable<Calendar> AccessibleCalendars(Guid userId) =>
        db.Calendars.Where(c => c.OwnerId == userId
            || db.CalendarShares.Any(s => s.CalendarId == c.Id && s.UserId == userId));

    public IQueryable<AddressBook> AccessibleAddressBooks(Guid userId) =>
        db.AddressBooks.Where(a => a.OwnerId == userId
            || db.AddressBookShares.Any(s => s.AddressBookId == a.Id && s.UserId == userId));

    public async Task<bool> CanReadCalendarAsync(Guid userId, Guid calendarId, CancellationToken ct = default) =>
        await db.Calendars.AnyAsync(c => c.Id == calendarId && c.OwnerId == userId, ct)
        || await db.CalendarShares.AnyAsync(s => s.CalendarId == calendarId && s.UserId == userId, ct);

    public async Task<bool> CanWriteCalendarAsync(Guid userId, Guid calendarId, CancellationToken ct = default) =>
        await db.Calendars.AnyAsync(c => c.Id == calendarId && c.OwnerId == userId, ct)
        || await db.CalendarShares.AnyAsync(s => s.CalendarId == calendarId && s.UserId == userId && s.Access == "read-write", ct);

    public async Task<bool> CanReadAddressBookAsync(Guid userId, Guid addressBookId, CancellationToken ct = default) =>
        await db.AddressBooks.AnyAsync(a => a.Id == addressBookId && a.OwnerId == userId, ct)
        || await db.AddressBookShares.AnyAsync(s => s.AddressBookId == addressBookId && s.UserId == userId, ct);

    public async Task<bool> CanWriteAddressBookAsync(Guid userId, Guid addressBookId, CancellationToken ct = default) =>
        await db.AddressBooks.AnyAsync(a => a.Id == addressBookId && a.OwnerId == userId, ct)
        || await db.AddressBookShares.AnyAsync(s => s.AddressBookId == addressBookId && s.UserId == userId && s.Access == "read-write", ct);
}
