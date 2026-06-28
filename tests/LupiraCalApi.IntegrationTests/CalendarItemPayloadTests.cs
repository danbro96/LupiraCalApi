using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class CalendarItemPayloadTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static SetItemPromptRequest Prompt(string instruction = "fill in the venue") => new()
    {
        Intent = PromptIntent.EnrichRecord,
        Instruction = instruction,
        Output = OutputKind.RecordEdit,
        Tier = ModelTier.Small,
        Fire = new PromptFire(PromptFireKind.OnStart, null, null),
    };

    private static SetItemActionRequest Action() => new()
    {
        Kind = ActionKind.SendCheckIn,
        ParamsJson = """{"message":"how did it go?"}""",
        Fire = new PromptFire(PromptFireKind.OnEnd, null, null),
    };

    private static async Task<CalendarItemDto> CreateItemAsync(HttpClient api, Guid calId)
    {
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest { CalendarId = calId, Title = "Mtg", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC" });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    [Fact]
    public async Task Set_then_clear_prompt_round_trips()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, calId);

        var set = await api.PutAsJsonAsync($"/items/{item.Id}/prompt", Prompt());
        set.EnsureSuccessStatusCode();
        var withPrompt = (await set.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.NotNull(withPrompt.Prompt);
        Assert.Equal(PromptIntent.EnrichRecord, withPrompt.Prompt!.Intent);
        Assert.Null(withPrompt.Action);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/items/{item.Id}/prompt")).StatusCode);
        var cleared = await api.GetFromJsonAsync<CalendarItemDto>($"/items/{item.Id}");
        Assert.Null(cleared!.Prompt);
    }

    [Fact]
    public async Task Set_action_round_trips()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, calId);

        var set = await api.PutAsJsonAsync($"/items/{item.Id}/action", Action());
        set.EnsureSuccessStatusCode();
        var withAction = (await set.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.NotNull(withAction.Action);
        Assert.Equal(ActionKind.SendCheckIn, withAction.Action!.Kind);
        Assert.Null(withAction.Prompt);
    }

    [Fact]
    public async Task Setting_an_action_on_a_prompted_item_is_conflict()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, calId);

        (await api.PutAsJsonAsync($"/items/{item.Id}/prompt", Prompt())).EnsureSuccessStatusCode();
        var conflict = await api.PutAsJsonAsync($"/items/{item.Id}/action", Action());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Setting_a_prompt_on_an_actioned_item_is_conflict()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, calId);

        (await api.PutAsJsonAsync($"/items/{item.Id}/action", Action())).EnsureSuccessStatusCode();
        var conflict = await api.PutAsJsonAsync($"/items/{item.Id}/prompt", Prompt());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Replacing_a_prompt_after_clearing_the_action_succeeds()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, calId);

        (await api.PutAsJsonAsync($"/items/{item.Id}/action", Action())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/items/{item.Id}/action")).StatusCode);

        var set = await api.PutAsJsonAsync($"/items/{item.Id}/prompt", Prompt());
        set.EnsureSuccessStatusCode();
        var dto = (await set.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.NotNull(dto.Prompt);
        Assert.Null(dto.Action);
    }

    [Fact]
    public async Task Payload_is_never_projected_to_dav()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, calId);
        const string secret = "DAV-MUST-NOT-LEAK-THIS-INSTRUCTION";
        (await api.PutAsJsonAsync($"/items/{item.Id}/prompt", Prompt(secret))).EnsureSuccessStatusCode();

        var dav = Factory.DavClient(Email);
        var ics = await (await dav.GetAsync($"/dav/u/{uid}/cal/{calId}/{item.IcalUid}.ics")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, ics);
    }
}
