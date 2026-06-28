using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Calendars;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class CalendarClassificationTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task User_created_calendar_defaults_to_agenda_generic()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);

        var containers = await api.GetFromJsonAsync<List<ContainerDto>>("/calendars");
        var cal = Assert.Single(containers!, c => c.Id == calId);
        Assert.Equal(CalendarClass.Agenda, cal.Class);
        Assert.Equal(CalendarKind.Generic, cal.Kind);
    }

    [Fact]
    public async Task Create_honors_explicit_class_and_kind()
    {
        var api = Factory.ApiClient(Email);
        var resp = await api.PostAsJsonAsync("/calendars",
            new CreateCalendarRequest { Slug = "ops", DisplayName = "Ops", Type = "calendar", Class = CalendarClass.System, Kind = CalendarKind.DevOps });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<ContainerDto>())!;
        Assert.Equal(CalendarClass.System, dto.Class);
        Assert.Equal(CalendarKind.DevOps, dto.Kind);
    }

    [Fact]
    public async Task Address_book_has_no_class_or_kind()
    {
        var api = Factory.ApiClient(Email);
        var bookId = await CreateAddressBookAsync(api);

        var containers = await api.GetFromJsonAsync<List<ContainerDto>>("/calendars");
        var book = Assert.Single(containers!, c => c.Id == bookId);
        Assert.Equal("addressbook", book.Type);
        Assert.Null(book.Class);
        Assert.Null(book.Kind);
    }

    [Fact]
    public async Task Bootstrap_seeds_the_standard_agenda_and_system_set()
    {
        var api = Factory.ApiClient(Email);
        var seeded = await (await api.PostAsync("/me/bootstrap", null)).Content.ReadFromJsonAsync<List<ContainerDto>>();

        var calendars = seeded!.Where(c => c.Type == "calendar").ToList();
        Assert.Equal(8, calendars.Count);   // FoodPlan is deferred (enum value only)
        foreach (var kind in new[] { CalendarKind.Personal, CalendarKind.Group, CalendarKind.Birthdays, CalendarKind.Availability })
            Assert.Contains(calendars, c => c.Kind == kind && c.Class == CalendarClass.Agenda);
        foreach (var kind in new[] { CalendarKind.Inbox, CalendarKind.LlmPrompts, CalendarKind.UserCheckIn, CalendarKind.DevOps })
            Assert.Contains(calendars, c => c.Kind == kind && c.Class == CalendarClass.System);
        Assert.DoesNotContain(calendars, c => c.Kind == CalendarKind.FoodPlan);
        Assert.Contains(seeded!, c => c.Type == "addressbook" && c.Slug == "personal");
    }

    [Fact]
    public async Task System_calendars_are_not_dav_projected()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var seeded = await (await api.PostAsync("/me/bootstrap", null)).Content.ReadFromJsonAsync<List<ContainerDto>>();
        var personal = seeded!.Single(c => c.Kind == CalendarKind.Personal);
        var inbox = seeded!.Single(c => c.Kind == CalendarKind.Inbox);

        var dav = Factory.DavClient(Email);
        var doc = await ReadXml(await SendDav(dav, "PROPFIND", $"/dav/u/{uid}/cal/", depth: "1"));
        var hrefs = doc.Descendants(D + "href").Select(h => h.Value).ToList();

        Assert.Contains(hrefs, h => h.Contains($"/cal/{personal.Id}/"));      // agenda → projected
        Assert.DoesNotContain(hrefs, h => h.Contains($"/cal/{inbox.Id}/"));   // system → hidden
    }

    [Fact]
    public async Task Direct_propfind_of_a_system_calendar_is_empty()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var seeded = await (await api.PostAsync("/me/bootstrap", null)).Content.ReadFromJsonAsync<List<ContainerDto>>();
        var inbox = seeded!.Single(c => c.Kind == CalendarKind.Inbox);

        var dav = Factory.DavClient(Email);
        var doc = await ReadXml(await SendDav(dav, "PROPFIND", $"/dav/u/{uid}/cal/{inbox.Id}/", depth: "0"));
        Assert.Empty(doc.Descendants(D + "response"));
    }
}
