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

    private static readonly DateTimeOffset Start = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private Task<Guid> CreateCalForAsync(Guid principal, string slug) =>
        InScope(async sp => (await sp.GetRequiredService<CalendarService>()
            .CreateAsync(principal, new CreateCalendarRequest { Slug = slug, DisplayName = slug, Type = "calendar", DefaultTimezone = "UTC" })).Value!.Id);

    private Task<Guid> CreateItemAsync(Guid principal, Guid? calId, string title) =>
        InScope(async sp => (await sp.GetRequiredService<CalendarItemService>()
            .CreateAsync(principal, new CreateCalendarItemRequest { CalendarId = calId, Title = title, IsAllDay = false, StartsAt = Start, EndsAt = Start.AddHours(1), StartTimezone = "UTC" })).Value!.Id);

    private Task<OpResult<List<FileItemResult>>> FileBatchAsync(Guid caller, IReadOnlyList<FileItemRequest> entries) =>
        InScope(sp => sp.GetRequiredService<CurationService>().AddToCalendarBatchAsync(caller, entries));

    [Fact]
    public async Task File_batch_files_each_entry_and_reports_per_entry_status_in_order()
    {
        var alice = await ProvisionAsync("sub-alice", "alice@x.test");
        var calId = await CreateCalForAsync(alice, "work");
        var i1 = await CreateItemAsync(alice, null, "One");
        var i2 = await CreateItemAsync(alice, null, "Two");
        var missing = Guid.NewGuid();

        var res = (await FileBatchAsync(alice,
        [
            new FileItemRequest { ItemId = i1, CalendarId = calId, Status = "accepted" },
            new FileItemRequest { ItemId = i2, CalendarId = calId, Status = "proposed" },
            new FileItemRequest { ItemId = missing, CalendarId = calId },
        ])).Value!;

        Assert.Equal(["filed", "filed", "notfound"], res.Select(r => r.Status));   // per-entry, one bad entry does not abort
        Assert.Equal([i1, i2, missing], res.Select(r => r.ItemId));                // input order preserved

        await using var s = Factory.Store.LightweightSession();
        Assert.Equal(CalendarEntryStatus.Accepted, (await s.LoadAsync<CalendarItem>(i1))!.Calendars.Single(m => m.CalendarId == calId).Status);
        Assert.Equal(CalendarEntryStatus.Proposed, (await s.LoadAsync<CalendarItem>(i2))!.Calendars.Single(m => m.CalendarId == calId).Status);
    }

    [Fact] // security: the single-item IDOR guard must hold through the batch path (opaque notfound, never filed)
    public async Task File_batch_cannot_file_another_users_item_and_preserves_never_abort()
    {
        var alice = await ProvisionAsync("sub-alice", "alice@x.test");
        var calA = await CreateCalForAsync(alice, "a-cal");
        var aliceItem = await CreateItemAsync(alice, calA, "A secret");

        var bob = await ProvisionAsync("sub-bob", "bob@x.test");
        var calB = await CreateCalForAsync(bob, "b-cal");
        var bobItem = await CreateItemAsync(bob, null, "Bob unfiled");

        var res = (await FileBatchAsync(bob,
        [
            new FileItemRequest { ItemId = aliceItem, CalendarId = calB, Status = "accepted" },   // IDOR attempt → opaque notfound
            new FileItemRequest { ItemId = bobItem, CalendarId = calB, Status = "accepted" },      // legit → filed (batch not aborted by the bad entry)
            new FileItemRequest { ItemId = bobItem, CalendarId = calA, Status = "accepted" },      // no write to alice's calendar → forbidden
        ])).Value!;

        Assert.Equal(["notfound", "filed", "forbidden"], res.Select(r => r.Status));

        // Bob still cannot read Alice's item — filing it did not self-grant access.
        var read = await InScope(sp => sp.GetRequiredService<CalendarItemService>().GetAsync(bob, aliceItem));
        Assert.Equal(OpStatus.Forbidden, read.Status);
    }

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
    public async Task Inviting_the_same_contact_twice_is_idempotent()
    {
        var principal = Guid.NewGuid();
        var calId = (await InScope(sp => sp.GetRequiredService<CalendarService>()
            .CreateAsync(principal, new CreateCalendarRequest { Slug = "w", DisplayName = "W", Type = "calendar", DefaultTimezone = "UTC" }))).Value!.Id;

        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var itemId = (await InScope(sp => sp.GetRequiredService<CalendarItemService>()
            .CreateAsync(principal, new CreateCalendarItemRequest { CalendarId = calId, Title = "Mtg", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC" }))).Value!.Id;

        var contact = Guid.NewGuid();
        _ = await InScope(sp => sp.GetRequiredService<ParticipationService>().InviteAsync(principal, itemId, contact, "req-participant"));
        _ = await InScope(sp => sp.GetRequiredService<ParticipationService>().InviteAsync(principal, itemId, contact, "req-participant"));

        await using var session = Factory.Store.LightweightSession();
        var att = Assert.Single((await session.LoadAsync<CalendarItem>(itemId))!.Attendees);
        Assert.Equal(contact, att.ContactId);
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
