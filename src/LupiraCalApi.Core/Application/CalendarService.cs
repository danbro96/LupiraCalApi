using LupiraCalApi.Auth;
using LupiraCalApi.Data;
using LupiraCalApi.Data.Entities;
using LupiraCalApi.Dtos.Calendars;
using Microsoft.EntityFrameworkCore;

namespace LupiraCalApi.Application;

/// <summary>Lists and creates the containers (calendars + address books) a user can access.</summary>
public sealed class CalendarService(CalDbContext db, AccessResolver access)
{
    public async Task<OpResult<List<ContainerDto>>> ListContainersAsync(Guid userId, CancellationToken ct = default)
    {
        var calendars = await access.AccessibleCalendars(userId)
            .Select(c => new ContainerDto(c.Id, "calendar", c.OwnerId, c.Slug, c.DisplayName, c.OwnerId == userId ? "owner" : "shared"))
            .ToListAsync(ct);
        var books = await access.AccessibleAddressBooks(userId)
            .Select(a => new ContainerDto(a.Id, "addressbook", a.OwnerId, a.Slug, a.DisplayName, a.OwnerId == userId ? "owner" : "shared"))
            .ToListAsync(ct);
        return OpResult<List<ContainerDto>>.Ok([.. calendars, .. books]);
    }

    public async Task<OpResult<ContainerDto>> CreateAsync(Guid userId, CreateCalendarRequest r, CancellationToken ct = default)
    {
        if (string.Equals(r.Kind, "addressbook", StringComparison.OrdinalIgnoreCase))
        {
            var a = new AddressBook { Id = Guid.NewGuid(), OwnerId = userId, Slug = r.Slug, DisplayName = r.DisplayName };
            db.AddressBooks.Add(a);
            await db.SaveChangesAsync(ct);
            return OpResult<ContainerDto>.Ok(new ContainerDto(a.Id, "addressbook", a.OwnerId, a.Slug, a.DisplayName, "owner"));
        }

        var c = new Calendar
        {
            Id = Guid.NewGuid(), OwnerId = userId, Slug = r.Slug,
            DisplayName = r.DisplayName, Color = r.Color, DefaultTimezone = r.DefaultTimezone,
        };
        db.Calendars.Add(c);
        await db.SaveChangesAsync(ct);
        return OpResult<ContainerDto>.Ok(new ContainerDto(c.Id, "calendar", c.OwnerId, c.Slug, c.DisplayName, "owner"));
    }
}
