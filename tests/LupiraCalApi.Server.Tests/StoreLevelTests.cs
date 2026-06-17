using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Calendars;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupiraCalApi.Server.Tests;

/// <summary>Integration tests for behaviors not exposed over HTTP — driven through the Core services + Marten store
/// resolved from the host DI. Validates the real event-append → projection → read path against Postgres.</summary>
public sealed class StoreLevelTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> f)
    {
        using var scope = Factory.Services.CreateScope();
        return await f(scope.ServiceProvider);
    }

    [Fact]
    public async Task Participation_history_is_composed_from_events()
    {
        var principal = Guid.NewGuid();
        var calId = (await InScope(sp => sp.GetRequiredService<CalendarService>()
            .CreateAsync(principal, new CreateCalendarRequest("w", "W", "calendar", null, "UTC")))).Value!.Id;

        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var itemId = (await InScope(sp => sp.GetRequiredService<CalendarItemService>()
            .CreateAsync(principal, new CreateCalendarItemRequest(calId, "Mtg", null, null, null, false, start, start.AddHours(1), "UTC", null, null, null, null, null)))).Value!.Id;

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
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var calId = (await InScope(sp => sp.GetRequiredService<CalendarService>()
            .CreateAsync(alice, new CreateCalendarRequest("fam", "Family", "calendar", null, "UTC")))).Value!.Id;

        await using (var s = Factory.Store.LightweightSession())
        {
            s.Store(new CalendarOwner { Id = CalendarOwner.MakeId(calId, bob), CalendarId = calId, PrincipalId = bob, Access = Access.Owner });
            await s.SaveChangesAsync();
        }

        Assert.True(await InScope(sp => sp.GetRequiredService<AccessResolver>().CanReadCalendarAsync(bob, calId)));
        var bobs = await InScope(sp => sp.GetRequiredService<CalendarService>().ListContainersAsync(bob));
        Assert.Contains(bobs.Value!, c => c.Id == calId);
    }
}
