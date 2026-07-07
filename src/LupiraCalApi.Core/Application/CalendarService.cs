using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Calendars;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>Lists and creates the containers (calendars + address books) a principal can access, and shares them by
/// granting/revoking co-owners. Creation grants the caller <c>owner</c>; sharing is owner-only and targets a member by email.</summary>
public sealed class CalendarService(IDocumentSession session, PrincipalDirectory principals, AccessResolver access)
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
        result.AddRange(cals.Select(c => new ContainerDto { Id = c.Id, Type = "calendar", Slug = c.Slug, DisplayName = c.DisplayName, Color = c.Color, DefaultTimezone = c.DefaultTimezone, Class = c.Class, Kind = c.Kind, Access = calAccess[c.Id] }));
        result.AddRange(books.Select(b => new ContainerDto { Id = b.Id, Type = "addressbook", Slug = b.Slug, DisplayName = b.DisplayName, Access = bookAccess[b.Id] }));
        return OpResult<List<ContainerDto>>.Ok(result);
    }

    public async Task<OpResult<ContainerDto>> CreateAsync(Guid principalId, CreateCalendarRequest r, CancellationToken ct = default)
    {
        if (string.Equals(r.Type, "addressbook", StringComparison.OrdinalIgnoreCase))
        {
            var b = new AddressBook { Id = Guid.NewGuid(), Slug = r.Slug, DisplayName = r.DisplayName };
            session.Store(b);
            session.Store(new AddressBookOwner { Id = AddressBookOwner.MakeId(b.Id, principalId), AddressBookId = b.Id, PrincipalId = principalId, Access = Access.Owner });
            await session.SaveChangesAsync(ct);
            return OpResult<ContainerDto>.Ok(new ContainerDto { Id = b.Id, Type = "addressbook", Slug = b.Slug, DisplayName = b.DisplayName, Access = Access.Owner });
        }

        var cls = r.Class ?? CalendarClass.Agenda;
        var kind = r.Kind ?? CalendarKind.Generic;
        var c = new Calendar { Id = Guid.NewGuid(), Slug = r.Slug, DisplayName = r.DisplayName, Color = r.Color, DefaultTimezone = r.DefaultTimezone, Class = cls, Kind = kind };
        session.Store(c);
        session.Store(new CalendarOwner { Id = CalendarOwner.MakeId(c.Id, principalId), CalendarId = c.Id, PrincipalId = principalId, Access = Access.Owner });
        await session.SaveChangesAsync(ct);
        return OpResult<ContainerDto>.Ok(new ContainerDto { Id = c.Id, Type = "calendar", Slug = c.Slug, DisplayName = c.DisplayName, Color = c.Color, DefaultTimezone = c.DefaultTimezone, Class = cls, Kind = kind, Access = Access.Owner });
    }

    /// <summary>The agenda + system calendars seeded per principal. FoodPlan is deferred (enum value only, not seeded).</summary>
    private static readonly (string Slug, string Name, CalendarClass Class, CalendarKind Kind)[] StandardCalendars =
    [
        ("personal", "Personal", CalendarClass.Agenda, CalendarKind.Personal),
        ("group", "Group", CalendarClass.Agenda, CalendarKind.Group),
        ("birthdays", "Birthdays", CalendarClass.Agenda, CalendarKind.Birthdays),
        ("availability", "Availability", CalendarClass.Agenda, CalendarKind.Availability),
        ("inbox", "Inbox", CalendarClass.System, CalendarKind.Inbox),
        ("llm-prompts", "LLM Prompts", CalendarClass.System, CalendarKind.LlmPrompts),
        ("user-checkin", "Check-ins", CalendarClass.System, CalendarKind.UserCheckIn),
        ("devops", "DevOps", CalendarClass.System, CalendarKind.DevOps),
    ];

    /// <summary>Ensures the caller has the standard calendar set (agenda + system) and a <c>personal</c> address book;
    /// idempotent — calendars are matched on <see cref="CalendarKind"/>, so a second call creates nothing.</summary>
    public async Task<OpResult<List<ContainerDto>>> BootstrapPersonalAsync(Guid principalId, CancellationToken ct = default)
    {
        var existing = (await ListContainersAsync(principalId, ct)).Value!;

        var result = new List<ContainerDto>();
        foreach (var (slug, name, cls, kind) in StandardCalendars)
            result.Add(existing.FirstOrDefault(c => c.Type == "calendar" && c.Kind == kind)
                ?? (await CreateAsync(principalId, new CreateCalendarRequest { Slug = slug, DisplayName = name, Type = "calendar", Class = cls, Kind = kind, DefaultTimezone = "UTC" }, ct)).Value!);

        result.Add(existing.FirstOrDefault(c => c.Type == "addressbook" && c.Slug == "personal")
            ?? (await CreateAsync(principalId, new CreateCalendarRequest { Slug = "personal", DisplayName = "Personal", Type = "addressbook" }, ct)).Value!);

        return OpResult<List<ContainerDto>>.Ok(result);
    }

    public async Task<OpResult<OwnerGrantDto>> GrantCalendarOwnerAsync(Guid callerId, Guid calendarId, GrantOwnerRequest r, CancellationToken ct = default)
    {
        if (await session.LoadAsync<Calendar>(calendarId, ct) is null) return OpResult<OwnerGrantDto>.NotFound();
        if (!await access.IsCalendarOwnerAsync(callerId, calendarId, ct)) return OpResult<OwnerGrantDto>.Forbidden("Only an owner may grant access.");
        var email = (r.Email ?? "").Trim();
        if (email.Length == 0) return OpResult<OwnerGrantDto>.Invalid("Email is required.");
        var (ok, level) = AccessParsing.Parse(r.Access);
        if (!ok) return OpResult<OwnerGrantDto>.Invalid("Access must be owner, read-write, or read.");

        var target = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        // Deterministic id → re-granting upserts the access level instead of duplicating the grant.
        session.Store(new CalendarOwner { Id = CalendarOwner.MakeId(calendarId, target.Id), CalendarId = calendarId, PrincipalId = target.Id, Access = level });
        await session.SaveChangesAsync(ct);
        return OpResult<OwnerGrantDto>.Ok(new OwnerGrantDto { ContainerId = calendarId, Type = "calendar", PrincipalId = target.Id, Email = target.Email, Access = level });
    }

    public async Task<OpResult> RevokeCalendarOwnerAsync(Guid callerId, Guid calendarId, string email, CancellationToken ct = default)
    {
        if (await session.LoadAsync<Calendar>(calendarId, ct) is null) return OpResult.NotFound();
        if (!await access.IsCalendarOwnerAsync(callerId, calendarId, ct)) return OpResult.Forbidden("Only an owner may revoke access.");
        var target = await principals.FindByEmailAsync(email, ct);
        if (target is null) return OpResult.NotFound();

        var grants = await session.Query<CalendarOwner>().Where(o => o.CalendarId == calendarId).ToListAsync(ct);
        var targetGrant = grants.FirstOrDefault(o => o.PrincipalId == target.Id);
        if (targetGrant is null) return OpResult.NotFound();
        if (OwnerGrants.WouldOrphan(targetGrant.Access, [.. grants.Where(o => o.PrincipalId != target.Id).Select(o => o.Access)]))
            return OpResult.Conflict("Cannot remove the last owner.");

        session.Delete(targetGrant);
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    public async Task<OpResult<OwnerGrantDto>> GrantAddressBookOwnerAsync(Guid callerId, Guid addressBookId, GrantOwnerRequest r, CancellationToken ct = default)
    {
        if (await session.LoadAsync<AddressBook>(addressBookId, ct) is null) return OpResult<OwnerGrantDto>.NotFound();
        if (!await access.IsAddressBookOwnerAsync(callerId, addressBookId, ct)) return OpResult<OwnerGrantDto>.Forbidden("Only an owner may grant access.");
        var email = (r.Email ?? "").Trim();
        if (email.Length == 0) return OpResult<OwnerGrantDto>.Invalid("Email is required.");
        var (ok, level) = AccessParsing.Parse(r.Access);
        if (!ok) return OpResult<OwnerGrantDto>.Invalid("Access must be owner, read-write, or read.");

        var target = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        session.Store(new AddressBookOwner { Id = AddressBookOwner.MakeId(addressBookId, target.Id), AddressBookId = addressBookId, PrincipalId = target.Id, Access = level });
        await session.SaveChangesAsync(ct);
        return OpResult<OwnerGrantDto>.Ok(new OwnerGrantDto { ContainerId = addressBookId, Type = "addressbook", PrincipalId = target.Id, Email = target.Email, Access = level });
    }

    public async Task<OpResult> RevokeAddressBookOwnerAsync(Guid callerId, Guid addressBookId, string email, CancellationToken ct = default)
    {
        if (await session.LoadAsync<AddressBook>(addressBookId, ct) is null) return OpResult.NotFound();
        if (!await access.IsAddressBookOwnerAsync(callerId, addressBookId, ct)) return OpResult.Forbidden("Only an owner may revoke access.");
        var target = await principals.FindByEmailAsync(email, ct);
        if (target is null) return OpResult.NotFound();

        var grants = await session.Query<AddressBookOwner>().Where(o => o.AddressBookId == addressBookId).ToListAsync(ct);
        var targetGrant = grants.FirstOrDefault(o => o.PrincipalId == target.Id);
        if (targetGrant is null) return OpResult.NotFound();
        if (OwnerGrants.WouldOrphan(targetGrant.Access, [.. grants.Where(o => o.PrincipalId != target.Id).Select(o => o.Access)]))
            return OpResult.Conflict("Cannot remove the last owner.");

        session.Delete(targetGrant);
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }
}
