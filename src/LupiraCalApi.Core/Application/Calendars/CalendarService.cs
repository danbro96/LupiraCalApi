using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Application.Results;
using LupiraCalApi.Core.Auth;
using LupiraCalApi.Core.Domain.Calendars;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.Calendars;
using Marten;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Core.Application.Calendars;

/// <summary>Lists and creates the calendars a principal can access, and shares them by granting/revoking co-owners.
/// Creation grants the caller <c>owner</c>; sharing is owner-only and targets a member by email. Address books are
/// owned by LupiraContactApi. Every calendar carries an IANA zone: one the caller supplies, else
/// <see cref="ItemTimeZoneOptions.DefaultTimezone"/>.</summary>
public sealed class CalendarService(IDocumentSession session, PrincipalDirectory principals, AccessResolver access, IOptions<ItemTimeZoneOptions> zones)
{
    private const string InvalidZone = "DefaultTimezone must be an IANA time zone id (e.g. Europe/Stockholm).";

    public async Task<OpResult<List<ContainerDto>>> ListContainersAsync(Guid principalId, CancellationToken ct = default)
    {
        var calOwners = await session.Query<CalendarOwner>().Where(o => o.PrincipalId == principalId).ToListAsync(ct);
        var calIds = calOwners.Select(o => o.CalendarId).ToList();
        var cals = await session.Query<Calendar>().Where(c => calIds.Contains(c.Id)).ToListAsync(ct);
        var calAccess = calOwners.ToDictionary(o => o.CalendarId, o => o.Access);

        return OpResult<List<ContainerDto>>.Ok(
            [.. cals.Select(c => ToDto(c, calAccess[c.Id]))]);
    }

    public async Task<OpResult<ContainerDto>> CreateAsync(Guid principalId, CreateCalendarRequest r, CancellationToken ct = default)
    {
        if (string.Equals(r.Type, "addressbook", StringComparison.OrdinalIgnoreCase))
            return OpResult<ContainerDto>.Invalid("Address books are managed by LupiraContactApi.");

        var zone = r.DefaultTimezone ?? zones.Value.DefaultTimezone;
        if (!TimeZoneIds.IsIana(zone)) return OpResult<ContainerDto>.Invalid(InvalidZone);

        var c = new Calendar { Id = Guid.NewGuid(), Slug = r.Slug, DisplayName = r.DisplayName, Color = r.Color, DefaultTimezone = zone, Class = r.Class ?? CalendarClass.Agenda, Kind = r.Kind ?? CalendarKind.Generic };
        session.Store(c);
        session.Store(new CalendarOwner { Id = CalendarOwner.MakeId(c.Id, principalId), CalendarId = c.Id, PrincipalId = principalId, Access = Access.Owner });
        await session.SaveChangesAsync(ct);
        return OpResult<ContainerDto>.Ok(ToDto(c, Access.Owner));
    }

    /// <summary>Owner-only. A new zone applies to items written later without one; existing items keep theirs.</summary>
    public async Task<OpResult<ContainerDto>> UpdateAsync(Guid callerId, Guid calendarId, UpdateCalendarRequest r, CancellationToken ct = default)
    {
        if (await session.LoadAsync<Calendar>(calendarId, ct) is not { } c) return OpResult<ContainerDto>.NotFound();
        if (!await access.IsCalendarOwnerAsync(callerId, calendarId, ct)) return OpResult<ContainerDto>.Forbidden("Only an owner may change a calendar.");
        if (!TimeZoneIds.IsIana(r.DefaultTimezone)) return OpResult<ContainerDto>.Invalid(InvalidZone);

        c.DefaultTimezone = r.DefaultTimezone;
        session.Store(c);
        await session.SaveChangesAsync(ct);
        return OpResult<ContainerDto>.Ok(ToDto(c, Access.Owner));
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

    /// <summary>Ensures the caller has the standard calendar set (agenda + system); idempotent — calendars are
    /// matched on <see cref="CalendarKind"/>, so a second call creates nothing. <paramref name="defaultTimezone"/> applies
    /// only to calendars it creates.</summary>
    public async Task<OpResult<List<ContainerDto>>> BootstrapPersonalAsync(Guid principalId, string? defaultTimezone = null, CancellationToken ct = default)
    {
        if (defaultTimezone is not null && !TimeZoneIds.IsIana(defaultTimezone)) return OpResult<List<ContainerDto>>.Invalid(InvalidZone);

        var existing = (await ListContainersAsync(principalId, ct)).Value!;

        var result = new List<ContainerDto>();
        foreach (var (slug, name, cls, kind) in StandardCalendars)
            result.Add(existing.FirstOrDefault(c => c.Kind == kind)
                ?? (await CreateAsync(principalId, new CreateCalendarRequest { Slug = slug, DisplayName = name, Type = "calendar", Class = cls, Kind = kind, DefaultTimezone = defaultTimezone }, ct)).Value!);

        return OpResult<List<ContainerDto>>.Ok(result);
    }

    public async Task<OpResult<OwnerGrantDto>> GrantCalendarOwnerAsync(Guid callerId, Guid calendarId, GrantOwnerRequest r, CancellationToken ct = default)
    {
        if (await session.LoadAsync<Calendar>(calendarId, ct) is null) return OpResult<OwnerGrantDto>.NotFound();
        if (!await access.IsCalendarOwnerAsync(callerId, calendarId, ct)) return OpResult<OwnerGrantDto>.Forbidden("Only an owner may grant access.");
        var email = (r.Email ?? string.Empty).Trim();
        if (email.Length == 0) return OpResult<OwnerGrantDto>.Invalid("Email is required.");
        var (ok, level) = AccessParsing.Parse(r.Access);
        if (!ok) return OpResult<OwnerGrantDto>.Invalid("Access must be owner, read-write, or read.");

        var target = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        // Deterministic id → re-granting upserts the access level instead of duplicating the grant.
        session.Store(new CalendarOwner { Id = CalendarOwner.MakeId(calendarId, target.Id), CalendarId = calendarId, PrincipalId = target.Id, Access = level });
        await session.SaveChangesAsync(ct);
        return OpResult<OwnerGrantDto>.Ok(new OwnerGrantDto { ContainerId = calendarId, Type = "calendar", PrincipalId = target.Id, Email = target.Email, DisplayName = target.DisplayName, Access = level });
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

    private static ContainerDto ToDto(Calendar c, Access access) => new()
    {
        Id = c.Id, Type = "calendar", Slug = c.Slug, DisplayName = c.DisplayName, Color = c.Color, DefaultTimezone = c.DefaultTimezone, Class = c.Class, Kind = c.Kind, Access = access,
    };
}
