using LupiraCalApi.Application;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Calendars;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>The Birthdays calendar is not stored — cal-api synthesizes yearly all-day occurrences at read time from
/// LupiraContactApi (<see cref="IContactResolver.BirthdaysAsync"/>). Year-less birthdays still recur; Feb-29 only in
/// leap years; when contacts are unavailable (the default null resolver) the calendar is simply empty.</summary>
public sealed class BirthdaysTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private sealed class StubContacts(params ContactBirthday[] birthdays) : IContactResolver
    {
        public bool IsConfigured => true;
        public Task<IReadOnlyList<ContactSummary>?> ResolveAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ContactSummary>?>([.. birthdays.Select(b => new ContactSummary(b.ContactId, b.DisplayName))]);
        public Task<IReadOnlyList<ContactBirthday>?> BirthdaysAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ContactBirthday>?>(birthdays);
    }

    private HttpClient ClientWith(IContactResolver contacts)
    {
        var scoped = Factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton(contacts)));
        var api = scoped.CreateClient();
        api.DefaultRequestHeaders.Add("X-Dev-User", Email);
        return api;
    }

    private static async Task<Guid> BirthdaysCalendarAsync(HttpClient api)
    {
        (await api.PostAsync("/me/bootstrap", null)).EnsureSuccessStatusCode();
        var cals = await api.GetFromJsonAsync<List<ContainerDto>>("/calendars");
        return cals!.Single(c => c.Kind == CalendarKind.Birthdays).Id;
    }

    private static async Task<List<CalendarItemOccurrenceDto>> SearchAsync(HttpClient api, Guid calId, DateTimeOffset from, DateTimeOffset to)
    {
        var url = $"/items?calendarId={calId}&from={Uri.EscapeDataString(from.ToString("o"))}&to={Uri.EscapeDataString(to.ToString("o"))}";
        return (await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>(url))!;
    }

    [Fact]
    public async Task Stored_items_cannot_be_created_in_or_filed_into_the_birthdays_calendar()
    {
        var api = ClientWith(new StubContacts());
        var bdays = await BirthdaysCalendarAsync(api);
        var start = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

        var create = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = bdays, Title = "Shadow", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC",
        });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, create.StatusCode);

        var elsewhere = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            Title = "Loose", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC",
        });
        elsewhere.EnsureSuccessStatusCode();
        var item = (await elsewhere.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        var file = await api.PostAsync($"/items/{item.Id}/calendars/{bdays}?status=accepted", null);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, file.StatusCode);
    }

    [Fact]
    public async Task Synthesizes_yearly_all_day_birthday_occurrences_from_contacts()
    {
        var ada = new ContactBirthday(Guid.NewGuid(), "Ada Byron", 1990, 3, 15);
        var grace = new ContactBirthday(Guid.NewGuid(), "Grace Hopper", null, 12, 24);   // year-less
        var leap = new ContactBirthday(Guid.NewGuid(), "Leap Kid", 2000, 2, 29);         // absent in 2026 (non-leap)
        var api = ClientWith(new StubContacts(ada, grace, leap));
        var bdays = await BirthdaysCalendarAsync(api);

        var occ = await SearchAsync(api, bdays,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero));

        Assert.Collection(occ.OrderBy(o => o.Start),
            o =>
            {
                Assert.Equal("Ada Byron's birthday", o.Title);
                Assert.True(o.IsAllDay);
                Assert.Equal(new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero), o.Start);
                Assert.Null(o.Completeness);   // birthdays are exempt from completeness
                Assert.Contains(bdays, o.CalendarIds);
                Assert.Equal(OriginKind.Birthday, o.Origin?.Kind);   // provenance links back to the contact
                Assert.Equal(ada.ContactId, o.Origin?.SourceId);
            },
            o => Assert.Equal("Grace Hopper's birthday", o.Title));   // Leap Kid skipped — 2026 has no Feb 29
    }

    [Fact]
    public async Task Feb29_birthday_lands_in_the_leap_year()
    {
        var leap = new ContactBirthday(Guid.NewGuid(), "Leap Kid", 2000, 2, 29);
        var api = ClientWith(new StubContacts(leap));
        var bdays = await BirthdaysCalendarAsync(api);

        var occ = await SearchAsync(api, bdays,
            new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2028, 12, 31, 23, 59, 59, TimeSpan.Zero));

        var only = Assert.Single(occ);
        Assert.Equal(new DateTimeOffset(2028, 2, 29, 0, 0, 0, TimeSpan.Zero), only.Start);
    }

    [Fact]
    public async Task No_birthdays_when_scoped_to_a_non_birthday_calendar()
    {
        var api = ClientWith(new StubContacts(new ContactBirthday(Guid.NewGuid(), "Ada Byron", 1990, 3, 15)));
        await BirthdaysCalendarAsync(api);   // seeds the standard set (incl. Personal)
        var cals = await api.GetFromJsonAsync<List<ContainerDto>>("/calendars");
        var personal = cals!.Single(c => c.Kind == CalendarKind.Personal).Id;

        var occ = await SearchAsync(api, personal,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero));

        Assert.Empty(occ);
    }

    [Fact]
    public async Task Empty_when_contacts_are_unavailable()
    {
        var api = Factory.ApiClient(Email);   // default null resolver — contacts unconfigured
        var bdays = await BirthdaysCalendarAsync(api);

        var occ = await SearchAsync(api, bdays,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero));

        Assert.Empty(occ);
    }
}
