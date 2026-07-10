using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Calendars;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Integration tests for behaviors not exposed over HTTP — driven through the Core services + Marten store
/// resolved from the host DI. Validates the real event-append → projection → read path against Postgres.</summary>
public sealed class StoreLevelTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> f)
    {
        using var scope = Factory.Services.CreateScope();
        return await f(scope.ServiceProvider);
    }

    private Task<Guid> ProvisionAsync(string sub, string email) =>
        InScope(async sp => (await sp.GetRequiredService<PrincipalDirectory>().ResolveOrProvisionAsync(sub, email, null)).Id);

    private Task<OpResult<OwnerGrantDto>> GrantCalAsync(Guid caller, Guid calId, string email, string access = "owner") =>
        InScope(sp => sp.GetRequiredService<CalendarService>().GrantCalendarOwnerAsync(caller, calId, new GrantOwnerRequest { Email = email, Access = access }));

    private Task<OpResult> RevokeCalAsync(Guid caller, Guid calId, string email) =>
        InScope(sp => sp.GetRequiredService<CalendarService>().RevokeCalendarOwnerAsync(caller, calId, email));

    [Fact]
    public async Task Participation_history_is_composed_from_events()
    {
        var principal = Guid.NewGuid();
        var calId = (await InScope(sp => sp.GetRequiredService<CalendarService>()
            .CreateAsync(principal, new CreateCalendarRequest { Slug = "w", DisplayName = "W", Type = "calendar", DefaultTimezone = "UTC" }))).Value!.Id;

        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var itemId = (await InScope(sp => sp.GetRequiredService<CalendarItemService>()
            .CreateAsync(principal, new CreateCalendarItemRequest { CalendarId = calId, Title = "Mtg", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC" }))).Value!.Id;

        var contact = Guid.NewGuid();
        _ = await InScope(sp => sp.GetRequiredService<ParticipationService>().InviteAsync(principal, itemId, contact, "req-participant"));

        Guid participationId;
        await using (var s = Factory.Store.LightweightSession())
            participationId = (await s.LoadAsync<CalendarItem>(itemId))!.Attendees.Single().ParticipationId;

        _ = await InScope(sp => sp.GetRequiredService<ParticipationService>().RespondAsync(principal, itemId, participationId, "accepted"));
        _ = await InScope(sp => sp.GetRequiredService<ParticipationService>().ConfirmAttendanceAsync(principal, itemId, participationId));

        await using var session = Factory.Store.LightweightSession();
        var att = (await session.LoadAsync<CalendarItem>(itemId))!.Attendees.Single();
        Assert.Equal(contact, att.ContactId);
        Assert.Equal(ParticipationStatus.Accepted, att.Status);
        Assert.NotNull(att.InvitedAt);
        Assert.NotNull(att.RespondedAt);
        Assert.NotNull(att.AttendedAt);
        Assert.Null(att.LeftAt);
    }

    [Fact]
    public async Task Granting_a_second_owner_lets_them_read_the_calendar()
    {
        var alice = await ProvisionAsync("sub-alice", "alice@x.test");
        var calId = (await InScope(sp => sp.GetRequiredService<CalendarService>()
            .CreateAsync(alice, new CreateCalendarRequest { Slug = "fam", DisplayName = "Family", Type = "calendar", DefaultTimezone = "UTC" }))).Value!.Id;

        var grant = await GrantCalAsync(alice, calId, "bob@x.test");
        Assert.Equal(OpStatus.Ok, grant.Status);
        var bob = grant.Value!.PrincipalId;

        Assert.True(await InScope(sp => sp.GetRequiredService<AccessResolver>().CanReadCalendarAsync(bob, calId)));
        var bobs = await InScope(sp => sp.GetRequiredService<CalendarService>().ListContainersAsync(bob));
        Assert.Contains(bobs.Value!, c => c.Id == calId);
    }

    [Fact]
    public async Task Re_granting_updates_access_in_place()
    {
        var alice = await ProvisionAsync("sub-alice", "alice@x.test");
        var calId = (await InScope(sp => sp.GetRequiredService<CalendarService>()
            .CreateAsync(alice, new CreateCalendarRequest { Slug = "fam", DisplayName = "Family", Type = "calendar", DefaultTimezone = "UTC" }))).Value!.Id;

        var bob = (await GrantCalAsync(alice, calId, "bob@x.test", "owner")).Value!.PrincipalId;
        await GrantCalAsync(alice, calId, "bob@x.test", "read");   // downgrade

        await using var s = Factory.Store.LightweightSession();
        var rows = await s.Query<CalendarOwner>().Where(o => o.CalendarId == calId && o.PrincipalId == bob).ToListAsync();
        Assert.Single(rows);                       // deterministic MakeId → upsert, not a duplicate
        Assert.Equal(Access.Read, rows[0].Access);
    }

    [Fact]
    public async Task Revoking_drops_access()
    {
        var alice = await ProvisionAsync("sub-alice", "alice@x.test");
        var calId = (await InScope(sp => sp.GetRequiredService<CalendarService>()
            .CreateAsync(alice, new CreateCalendarRequest { Slug = "fam", DisplayName = "Family", Type = "calendar", DefaultTimezone = "UTC" }))).Value!.Id;
        var bob = (await GrantCalAsync(alice, calId, "bob@x.test")).Value!.PrincipalId;

        var revoke = await RevokeCalAsync(alice, calId, "bob@x.test");
        Assert.Equal(OpStatus.Ok, revoke.Status);
        Assert.False(await InScope(sp => sp.GetRequiredService<AccessResolver>().CanReadCalendarAsync(bob, calId)));
    }

    [Fact]
    public async Task Cannot_revoke_the_last_owner()
    {
        var alice = await ProvisionAsync("sub-alice", "alice@x.test");
        var calId = (await InScope(sp => sp.GetRequiredService<CalendarService>()
            .CreateAsync(alice, new CreateCalendarRequest { Slug = "fam", DisplayName = "Family", Type = "calendar", DefaultTimezone = "UTC" }))).Value!.Id;

        var revoke = await RevokeCalAsync(alice, calId, "alice@x.test");
        Assert.Equal(OpStatus.Conflict, revoke.Status);
    }

    [Fact]
    public async Task Granting_an_unprovisioned_email_converges_on_login()
    {
        var alice = await ProvisionAsync("sub-alice", "alice@x.test");
        var calId = (await InScope(sp => sp.GetRequiredService<CalendarService>()
            .CreateAsync(alice, new CreateCalendarRequest { Slug = "fam", DisplayName = "Family", Type = "calendar", DefaultTimezone = "UTC" }))).Value!.Id;

        var carolId = (await GrantCalAsync(alice, calId, "carol@x.test")).Value!.PrincipalId;   // placeholder principal
        var carol = await InScope(sp => sp.GetRequiredService<PrincipalDirectory>()
            .ResolveOrProvisionAsync("oidc-sub-carol", "carol@x.test", "Carol"));               // first real login

        Assert.Equal(carolId, carol.Id);                  // same row — did not create a duplicate
        Assert.Equal("oidc-sub-carol", carol.AuthentikSub); // placeholder sub upgraded to the real one
    }

    [Fact]
    public async Task Bootstrap_is_idempotent()
    {
        var alice = await ProvisionAsync("sub-alice", "alice@x.test");

        var first = await InScope(sp => sp.GetRequiredService<CalendarService>().BootstrapPersonalAsync(alice));
        Assert.Equal(8, first.Value!.Count);   // the 8 standard calendars
        Assert.Contains(first.Value!, c => c is { Type: "calendar", Kind: CalendarKind.Personal, Slug: "personal" });
        Assert.Contains(first.Value!, c => c is { Type: "calendar", Class: CalendarClass.System, Kind: CalendarKind.Inbox });

        var second = await InScope(sp => sp.GetRequiredService<CalendarService>().BootstrapPersonalAsync(alice));
        Assert.Equal(
            first.Value!.Select(c => c.Id).OrderBy(x => x),
            second.Value!.Select(c => c.Id).OrderBy(x => x));   // same ids, nothing new created

        var all = await InScope(sp => sp.GetRequiredService<CalendarService>().ListContainersAsync(alice));
        Assert.Equal(8, all.Value!.Count);
    }

}
