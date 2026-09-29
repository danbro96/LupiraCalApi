using System.Net;
using System.Net.Http.Json;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.CalendarItems.Events;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class ParticipationTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Invite_respond_attend_records_history()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        // Contacts live in LupiraContactApi; with the Null resolver an invite stores the bare Guid unvalidated.
        var contactId = Guid.NewGuid();

        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var create = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest { CalendarId = calId, Title = "Mtg", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC" });
        var itemId = (await create.Content.ReadFromJsonAsync<CalendarItemDto>())!.Id;

        (await api.PostAsync($"/items/{itemId}/participants?contactId={contactId}&role=req-participant", null)).EnsureSuccessStatusCode();

        Guid participationId;
        await using (var s = Factory.Store.LightweightSession())
            participationId = (await s.LoadAsync<CalendarItem>(itemId))!.Attendees.Single().ParticipationId;

        (await api.PostAsync($"/items/{itemId}/participants/{participationId}/respond?status=accepted", null)).EnsureSuccessStatusCode();
        (await api.PostAsync($"/items/{itemId}/participants/{participationId}/attend", null)).EnsureSuccessStatusCode();

        await using var session = Factory.Store.LightweightSession();
        var att = (await session.LoadAsync<CalendarItem>(itemId))!.Attendees.Single();
        Assert.Equal(contactId, att.ContactId);
        Assert.Equal(ParticipationStatus.Accepted, att.Status);
        Assert.NotNull(att.InvitedAt);
        Assert.NotNull(att.RespondedAt);
        Assert.NotNull(att.AttendedAt);
    }

    [Fact]
    public async Task Invite_reads_the_ical_role_and_rejects_unknown_roles_and_statuses()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var create = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest { CalendarId = calId, Title = "Mtg", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1) });
        var itemId = (await create.Content.ReadFromJsonAsync<CalendarItemDto>())!.Id;

        (await api.PostAsync($"/items/{itemId}/participants?contactId={Guid.NewGuid()}&role=opt-participant", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsync($"/items/{itemId}/participants?contactId={Guid.NewGuid()}&role=optional", null)).StatusCode);

        ItemAttendee att;
        await using (var s = Factory.Store.LightweightSession())
            att = (await s.LoadAsync<CalendarItem>(itemId))!.Attendees.Single();
        Assert.Equal(ParticipationRole.OptionalParticipant, att.Role);

        var respond = (string status) => api.PostAsync($"/items/{itemId}/participants/{att.ParticipationId}/respond?status={status}", null);
        Assert.Equal(HttpStatusCode.BadRequest, (await respond("maybe")).StatusCode);
        (await respond("needs-action")).EnsureSuccessStatusCode();
    }

    private async Task<(HttpClient Api, Guid ItemId, Guid ParticipationId, Guid ContactId)> InvitedAsync()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var create = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest { CalendarId = calId, Title = "Mtg", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1) });
        var itemId = (await create.Content.ReadFromJsonAsync<CalendarItemDto>())!.Id;
        var contactId = Guid.NewGuid();
        (await api.PostAsync($"/items/{itemId}/participants?contactId={contactId}", null)).EnsureSuccessStatusCode();
        await using var s = Store.LightweightSession();
        return (api, itemId, (await s.LoadAsync<CalendarItem>(itemId))!.Attendees.Single().ParticipationId, contactId);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient api, HttpMethod method, string path, Guid? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (idempotencyKey is { } key) request.Headers.Add("Idempotency-Key", key.ToString());
        return api.SendAsync(request);
    }

    private async Task<int> StreamLengthAsync(Guid itemId)
    {
        await using var s = Store.LightweightSession();
        return (await s.Events.FetchStreamAsync(itemId)).Count;
    }

    [Fact]
    public async Task A_redelivered_command_with_its_key_appends_nothing()
    {
        var (api, itemId, pid, _) = await InvitedAsync();
        var key = Guid.NewGuid();
        var before = await StreamLengthAsync(itemId);

        (await SendAsync(api, HttpMethod.Post, $"/items/{itemId}/participants/{pid}/respond?status=accepted", key)).EnsureSuccessStatusCode();
        (await SendAsync(api, HttpMethod.Post, $"/items/{itemId}/participants/{pid}/respond?status=declined", key)).EnsureSuccessStatusCode();

        Assert.Equal(before + 1, await StreamLengthAsync(itemId));
        await using var s = Store.LightweightSession();
        Assert.Equal(ParticipationStatus.Accepted, (await s.LoadAsync<CalendarItem>(itemId))!.Attendees.Single().Status);
    }

    [Fact]
    public async Task Re_asserting_the_current_state_appends_nothing()
    {
        var (api, itemId, pid, contactId) = await InvitedAsync();
        (await api.PostAsync($"/items/{itemId}/participants/{pid}/respond?status=accepted", null)).EnsureSuccessStatusCode();
        (await api.PostAsync($"/items/{itemId}/participants/{pid}/attend", null)).EnsureSuccessStatusCode();
        (await api.PostAsync($"/items/{itemId}/participants/{pid}/leave", null)).EnsureSuccessStatusCode();
        var before = await StreamLengthAsync(itemId);

        (await api.PostAsync($"/items/{itemId}/participants?contactId={contactId}", null)).EnsureSuccessStatusCode();
        (await api.PostAsync($"/items/{itemId}/participants/{pid}/respond?status=ACCEPTED", null)).EnsureSuccessStatusCode();
        (await api.PostAsync($"/items/{itemId}/participants/{pid}/attend", null)).EnsureSuccessStatusCode();
        (await api.PostAsync($"/items/{itemId}/participants/{pid}/leave", null)).EnsureSuccessStatusCode();

        Assert.Equal(before, await StreamLengthAsync(itemId));
    }

    [Fact]
    public async Task OccurredAt_stamps_when_the_client_acted()
    {
        var (api, itemId, pid, _) = await InvitedAsync();
        var actedAt = new DateTimeOffset(2026, 6, 30, 7, 15, 0, TimeSpan.Zero);

        (await api.PostAsync($"/items/{itemId}/participants/{pid}/respond?status=tentative&occurredAt={actedAt:yyyy-MM-ddTHH:mm:ssZ}", null)).EnsureSuccessStatusCode();

        await using var s = Store.LightweightSession();
        Assert.Equal(actedAt, (await s.LoadAsync<CalendarItem>(itemId))!.Attendees.Single().RespondedAt);
    }

    [Theory]
    [InlineData("POST", "respond?status=accepted")]
    [InlineData("POST", "attend")]
    [InlineData("POST", "leave")]
    [InlineData("DELETE", "")]
    public async Task A_participation_the_item_does_not_hold_is_not_found(string method, string action)
    {
        var (api, itemId, _, _) = await InvitedAsync();
        var path = $"/items/{itemId}/participants/{Guid.NewGuid()}" + (action.Length > 0 ? $"/{action}" : "");
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(api, new HttpMethod(method), path)).StatusCode);
    }

    [Fact]
    public async Task A_removal_succeeds_again_only_as_a_redelivery()
    {
        var (api, itemId, pid, _) = await InvitedAsync();
        var key = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(api, HttpMethod.Delete, $"/items/{itemId}/participants/{pid}", key)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(api, HttpMethod.Delete, $"/items/{itemId}/participants/{pid}", key)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(api, HttpMethod.Delete, $"/items/{itemId}/participants/{pid}")).StatusCode);
    }

    [Fact]
    public async Task Removing_by_contact_clears_every_row_it_holds_and_is_done_when_none()
    {
        var (api, itemId, _, contactId) = await InvitedAsync();
        // A legacy item where one contact holds two rows.
        await using (var s = Store.LightweightSession())
        {
            s.Events.Append(itemId, new AttendeeInvited(itemId, Guid.NewGuid(), contactId, ParticipationRole.RequiredParticipant, DateTimeOffset.UtcNow));
            await s.SaveChangesAsync();
        }

        (await SendAsync(api, HttpMethod.Delete, $"/items/{itemId}/participants?contactId={contactId}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(api, HttpMethod.Delete, $"/items/{itemId}/participants?contactId={contactId}")).StatusCode);

        await using var check = Store.LightweightSession();
        Assert.Empty((await check.LoadAsync<CalendarItem>(itemId))!.Attendees);
    }

    [Fact]
    public async Task Adding_a_set_tolerates_a_contact_holding_two_rows()
    {
        var (api, itemId, _, contactId) = await InvitedAsync();
        await using (var s = Store.LightweightSession())
        {
            s.Events.Append(itemId, new AttendeeInvited(itemId, Guid.NewGuid(), contactId, ParticipationRole.RequiredParticipant, DateTimeOffset.UtcNow));
            await s.SaveChangesAsync();
        }

        var resp = await api.PutAsJsonAsync($"/items/{itemId}/participants", new SetParticipantsRequest { ContactIds = [contactId, Guid.NewGuid()], Attended = false });

        resp.EnsureSuccessStatusCode();
        var result = (await resp.Content.ReadFromJsonAsync<SetParticipantsResult>())!;
        Assert.Equal(1, result.AlreadyPresent);
        Assert.Single(result.Added);
    }

    [Fact]
    public async Task Invite_on_a_missing_item_is_not_found()
    {
        var api = Factory.ApiClient(Email);
        var resp = await api.PostAsync($"/items/{Guid.NewGuid()}/participants?contactId={Guid.NewGuid()}&role=req-participant", null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
