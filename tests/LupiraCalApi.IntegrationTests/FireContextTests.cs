using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Calendars;
using LupiraCalApi.Dtos.CalendarItems;
using Marten;
using Npgsql;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Materialisation stamps calendar_id + principal_id + expire_after from ONE resolved fire calendar
/// (exercised through the projection — the public surface).</summary>
public sealed class FireContextTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private async Task RunMaterializerAsync()
    {
        using var daemon = await Factory.Store.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync("scheduled_fire", TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    private static SetItemPromptRequest Prompt() => new()
    {
        Intent = PromptIntent.Monitor,
        Instruction = "check in",
        Output = OutputKind.Summary,
        Fire = new PromptFire(PromptFireKind.OnStart, null, null),
    };

    private static async Task<CalendarItemDto> CreateItemAsync(HttpClient api, Guid? calId)
    {
        var start = DateTimeOffset.UtcNow.AddDays(10);
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = calId, Title = "Fire", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC",
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    [Fact]
    public async Task Accepted_membership_stamps_calendar_principal_and_expiry()
    {
        var api = Factory.ApiClient(Email);
        var myId = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateItemAsync(api, calId);

        (await api.PutAsJsonAsync($"/items/{item.Id}/prompt", Prompt())).EnsureSuccessStatusCode();
        await RunMaterializerAsync();

        var row = await SingleRowAsync(item.Id);
        Assert.Equal(calId, row.CalendarId);
        Assert.Equal(myId, row.PrincipalId);
        Assert.Equal(TimeSpan.FromHours(24), row.ExpireAfter);   // agenda calendar → fallback
    }

    [Fact]
    public async Task Proposed_only_membership_still_materializes_with_a_real_calendar()
    {
        // The bug-(b) regression: an item whose only membership is Proposed used to get calendar_id = Guid.Empty
        // and the 24h fallback even on a system calendar. REST can't author this state (item writes need an
        // Accepted membership), so the curation-produced events are appended directly.
        var api = Factory.ApiClient(Email);
        var myId = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var itemId = await AppendItemAsync(calId, CalendarEntryStatus.Proposed);

        await RunMaterializerAsync();

        var row = await SingleRowAsync(itemId);
        Assert.Equal(calId, row.CalendarId);
        Assert.Equal(myId, row.PrincipalId);
    }

    [Fact]
    public async Task No_membership_materializes_nothing()
    {
        // No calendar → no fire calendar and no principal to deliver as.
        var itemId = await AppendItemAsync(calendarId: null, CalendarEntryStatus.Proposed);

        await RunMaterializerAsync();

        Assert.Equal(0, await CountAsync(itemId));
    }

    [Fact]
    public async Task System_calendar_wins_and_drives_expiry()
    {
        var api = Factory.ApiClient(Email);
        var boot = await api.PostAsync("/me/bootstrap", null);
        boot.EnsureSuccessStatusCode();
        var calendars = (await boot.Content.ReadFromJsonAsync<List<ContainerDto>>())!;
        var llmPrompts = calendars.Single(c => c.Kind == CalendarKind.LlmPrompts).Id;
        var personal = calendars.Single(c => c.Kind == CalendarKind.Personal).Id;

        var item = await CreateItemAsync(api, personal);
        (await api.PostAsync($"/items/{item.Id}/calendars/{llmPrompts}/accept", null)).EnsureSuccessStatusCode();

        (await api.PutAsJsonAsync($"/items/{item.Id}/prompt", Prompt())).EnsureSuccessStatusCode();
        await RunMaterializerAsync();

        var row = await SingleRowAsync(item.Id);
        Assert.Equal(llmPrompts, row.CalendarId);              // system calendar preferred over the agenda one
        Assert.Equal(TimeSpan.FromHours(6), row.ExpireAfter);  // LlmPrompts expiry, from the SAME calendar
    }

    // ---- helpers ----

    /// <summary>Append the item events directly (create + optional membership + prompt) — states REST refuses to author.</summary>
    private async Task<Guid> AppendItemAsync(Guid? calendarId, CalendarEntryStatus status)
    {
        var itemId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow.AddDays(10);
        var fields = new CalendarItemFields("Fire", null, null, false, start, start.AddHours(1), "UTC", null, null, null, null, null, null, null, null);
        var prompt = new ItemPrompt(PromptIntent.Monitor, null, "check in", OutputKind.Summary, null, null, FallbackMode.Retry,
            new PromptFire(PromptFireKind.OnStart, null, null), true);

        await using var s = Factory.Store.LightweightSession();
        var events = new List<object> { new ItemScheduled(itemId, $"{itemId:N}@test", fields, null, "hash") };
        if (calendarId is { } calId) events.Add(new AddedToCalendar(itemId, calId, status, DateTimeOffset.UtcNow));
        events.Add(new ItemPromptSet(itemId, prompt));
        s.Events.StartStream<CalendarItem>(itemId, events.ToArray());
        await s.SaveChangesAsync();
        return itemId;
    }

    private sealed record FireRow(Guid CalendarId, Guid? PrincipalId, TimeSpan? ExpireAfter);

    private async Task<FireRow> SingleRowAsync(Guid itemId)
    {
        await using var conn = new NpgsqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select calendar_id, principal_id, expire_after from cal.scheduled_fire where item_id = @id", conn);
        cmd.Parameters.AddWithValue("id", itemId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "expected one scheduled_fire row");
        var row = new FireRow(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<TimeSpan>(2));
        Assert.False(await reader.ReadAsync(), "expected exactly one scheduled_fire row");
        return row;
    }

    private async Task<int> CountAsync(Guid itemId)
    {
        await using var conn = new NpgsqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select count(*) from cal.scheduled_fire where item_id = @id", conn);
        cmd.Parameters.AddWithValue("id", itemId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
