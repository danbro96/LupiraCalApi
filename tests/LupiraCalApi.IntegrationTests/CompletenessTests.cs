using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Contacts;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class CompletenessTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";
    static readonly DateTimeOffset Start = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private static async Task<CalendarItemDto> CreateItemAsync(HttpClient api, Guid calId, string title, string? location = null, string? description = null, string? category = null)
    {
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = calId, Title = title, Description = description, Location = location, Category = category,
            IsAllDay = false, StartsAt = Start, EndsAt = Start.AddHours(1), StartTimezone = "UTC",
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    [Fact]
    public async Task A_richer_item_scores_higher_than_a_thin_one()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        await CreateItemAsync(api, calId, "Thin");
        await CreateItemAsync(api, calId, "Rich", location: "HQ", description: "Quarterly planning with pre-reads");

        var from = Uri.EscapeDataString(Start.AddDays(-1).ToString("o"));
        var to = Uri.EscapeDataString(Start.AddDays(1).ToString("o"));
        var occ = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/items?calendarId={calId}&from={from}&to={to}");

        var thin = occ!.Single(o => o.Title == "Thin");
        var rich = occ!.Single(o => o.Title == "Rich");
        Assert.NotNull(thin.Completeness);
        Assert.NotNull(rich.Completeness);
        Assert.True(rich.Completeness!.Score > thin.Completeness!.Score);
        Assert.Contains(thin.Completeness.Gaps, g => g.Field == "location");   // heaviest gap surfaced
    }

    [Fact]
    public async Task Single_item_get_exposes_completeness_and_ranked_gaps()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, calId, "Bare", category: "Meeting");

        var got = await api.GetFromJsonAsync<CalendarItemDto>($"/items/{item.Id}");
        Assert.NotNull(got!.Completeness);
        // For a Meeting, location(2) and attendees(2) are the heaviest gaps and lead the list.
        Assert.Equal(["location", "attendees"], got.Completeness!.Gaps.Take(2).Select(g => g.Field));
    }

    [Fact]
    public async Task Items_in_system_calendars_are_exempt()
    {
        var api = Factory.ApiClient(Email);
        var seeded = await (await api.PostAsync("/me/bootstrap", null)).Content.ReadFromJsonAsync<List<LupiraCalApi.Dtos.Calendars.ContainerDto>>();
        var inbox = seeded!.Single(c => c.Kind == LupiraCalApi.Domain.CalendarKind.Inbox);

        var item = await CreateItemAsync(api, inbox.Id, "Captured");
        var got = await api.GetFromJsonAsync<CalendarItemDto>($"/items/{item.Id}");
        Assert.Null(got!.Completeness);   // system calendar → not applicable
    }

    [Fact]
    public async Task Contact_completeness_is_exposed_and_reflects_reach()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var withEmail = await CreateContactAsync(api, abId, "Jane", "Doe", email: "jane@x.test");
        var bare = await CreateContactAsync(api, abId, "John", "Roe");

        var withGot = await api.GetFromJsonAsync<ContactDto>($"/contacts/{withEmail.Id}");
        var bareGot = await api.GetFromJsonAsync<ContactDto>($"/contacts/{bare.Id}");

        Assert.NotNull(withGot!.Completeness);
        Assert.True(withGot.Completeness!.Score > bareGot!.Completeness!.Score);
        Assert.Contains(bareGot.Completeness!.Gaps, g => g.Field == "primaryReach" && g.Weight == 3);
    }

    [Fact]
    public async Task Contact_organisation_membership_closes_the_organisation_gap()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, abId, "Jane", "Doe", email: "jane@x.test");

        var before = await api.GetFromJsonAsync<ContactDto>($"/contacts/{contact.Id}");
        Assert.Contains(before!.Completeness!.Gaps, g => g.Field == "organisation");

        var group = (await (await api.PostAsync($"/address-books/{abId}/groups?kind=organization&name=Acme", null)).Content.ReadFromJsonAsync<ContactGroupDto>())!;
        (await api.PostAsync($"/groups/{group.Id}/members?contactId={contact.Id}", null)).EnsureSuccessStatusCode();

        var after = await api.GetFromJsonAsync<ContactDto>($"/contacts/{contact.Id}");
        Assert.DoesNotContain(after!.Completeness!.Gaps, g => g.Field == "organisation");
    }
}
