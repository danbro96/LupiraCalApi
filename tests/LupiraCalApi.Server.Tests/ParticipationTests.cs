using System.Net;
using System.Net.Http.Json;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using Marten;
using Xunit;

namespace LupiraCalApi.Server.Tests;

public sealed class ParticipationTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Invite_respond_attend_records_history()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var abId = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, abId);

        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var create = await api.PostAsJsonAsync("/api/items", new CreateCalendarItemRequest(calId, "Mtg", null, null, null, false, start, start.AddHours(1), "UTC", null, null, null, null, null));
        var itemId = (await create.Content.ReadFromJsonAsync<CalendarItemDto>())!.Id;

        (await api.PostAsync($"/api/items/{itemId}/participants?contactId={contact.Id}&role=req-participant", null)).EnsureSuccessStatusCode();

        Guid participationId;
        await using (var s = Factory.Store.LightweightSession())
            participationId = (await s.LoadAsync<CalendarItem>(itemId))!.Attendees.Single().ParticipationId;

        (await api.PostAsync($"/api/items/{itemId}/participants/{participationId}/respond?status=accepted", null)).EnsureSuccessStatusCode();
        (await api.PostAsync($"/api/items/{itemId}/participants/{participationId}/attend", null)).EnsureSuccessStatusCode();

        await using var session = Factory.Store.LightweightSession();
        var att = (await session.LoadAsync<CalendarItem>(itemId))!.Attendees.Single();
        Assert.Equal(contact.Id, att.ContactId);
        Assert.Equal(ParticipationStatus.Accepted, att.Status);
        Assert.NotNull(att.InvitedAt);
        Assert.NotNull(att.RespondedAt);
        Assert.NotNull(att.AttendedAt);
    }

    [Fact]
    public async Task Invite_on_a_missing_item_is_not_found()
    {
        var api = Factory.ApiClient(Email);
        var resp = await api.PostAsync($"/api/items/{Guid.NewGuid()}/participants?contactId={Guid.NewGuid()}&role=req-participant", null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
