using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Calendars;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>Lists and creates the containers (calendars + address books) a principal can access. Creation grants the caller <c>owner</c>.</summary>
public sealed class CalendarService(IDocumentSession session)
{
    public async Task<OpResult<List<ContainerDto>>> ListContainersAsync(Guid principalId, CancellationToken ct = default)
    {
        var calOwners = await session.Query<CalendarOwner>().Where(o => o.PrincipalId == principalId).ToListAsync(ct);
        var bookOwners = await session.Query<AddressBookOwner>().Where(o => o.PrincipalId == principalId).ToListAsync(ct);

        var calIds = calOwners.Select(o => o.CalendarId).ToList();
        var bookIds = bookOwners.Select(o => o.AddressBookId).ToList();
        var cals = await session.Query<Calendar>().Where(c => calIds.Contains(c.Id)).ToListAsync(ct);
        var books = await session.Query<AddressBook>().Where(b => bookIds.Contains(b.Id)).ToListAsync(ct);

        var calAccess = calOwners.ToDictionary(o => o.CalendarId, o => o.Access);
        var bookAccess = bookOwners.ToDictionary(o => o.AddressBookId, o => o.Access);

        var result = new List<ContainerDto>();
        result.AddRange(cals.Select(c => new ContainerDto(c.Id, "calendar", c.Slug, c.DisplayName, calAccess[c.Id].ToString())));
        result.AddRange(books.Select(b => new ContainerDto(b.Id, "addressbook", b.Slug, b.DisplayName, bookAccess[b.Id].ToString())));
        return OpResult<List<ContainerDto>>.Ok(result);
    }

    public async Task<OpResult<ContainerDto>> CreateAsync(Guid principalId, CreateCalendarRequest r, CancellationToken ct = default)
    {
        if (string.Equals(r.Kind, "addressbook", StringComparison.OrdinalIgnoreCase))
        {
            var b = new AddressBook { Id = Guid.NewGuid(), Slug = r.Slug, DisplayName = r.DisplayName };
            session.Store(b);
            session.Store(new AddressBookOwner { Id = AddressBookOwner.MakeId(b.Id, principalId), AddressBookId = b.Id, PrincipalId = principalId, Access = Access.Owner });
            await session.SaveChangesAsync(ct);
            return OpResult<ContainerDto>.Ok(new ContainerDto(b.Id, "addressbook", b.Slug, b.DisplayName, nameof(Access.Owner)));
        }

        var c = new Calendar { Id = Guid.NewGuid(), Slug = r.Slug, DisplayName = r.DisplayName, Color = r.Color, DefaultTimezone = r.DefaultTimezone };
        session.Store(c);
        session.Store(new CalendarOwner { Id = CalendarOwner.MakeId(c.Id, principalId), CalendarId = c.Id, PrincipalId = principalId, Access = Access.Owner });
        await session.SaveChangesAsync(ct);
        return OpResult<ContainerDto>.Ok(new ContainerDto(c.Id, "calendar", c.Slug, c.DisplayName, nameof(Access.Owner)));
    }
}
