using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Scheduling;
using LupiraCalApi.Serialization;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>The nightly sweep brings items into the rolling window — including one-shots that were beyond the
/// 35-day horizon at set-time (the bug-(a) regression: those used to be dropped forever).</summary>
public sealed class HorizonSweepTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private HorizonSweep Sweep() => new(Factory.Store, new FireMaterializer(new RecurrenceExpander()), NullLogger<HorizonSweep>.Instance);

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

    private async Task<Guid> CreatePromptItemAsync(HttpClient api, Guid calId, DateTimeOffset start, string? rrule = null)
    {
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = calId, Title = "Fire", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1),
            StartTimezone = "UTC", RecurrenceRule = rrule,
        });
        resp.EnsureSuccessStatusCode();
        var item = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        (await api.PutAsJsonAsync($"/items/{item.Id}/prompt", Prompt())).EnsureSuccessStatusCode();
        return item.Id;
    }

    [Fact]
    public async Task One_shot_beyond_the_horizon_is_picked_up_when_the_window_reaches_it()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var itemId = await CreatePromptItemAsync(api, calId, DateTimeOffset.UtcNow.AddDays(60));

        await RunMaterializerAsync();
        Assert.Equal(0, await CountAsync(itemId));                                  // 60d > 35d window at set-time

        await Sweep().SweepAsync(DateTimeOffset.UtcNow.AddDays(30), CancellationToken.None);
        Assert.Equal(1, await CountAsync(itemId));                                  // now inside now+30d .. +65d

        await Sweep().SweepAsync(DateTimeOffset.UtcNow.AddDays(30), CancellationToken.None);
        Assert.Equal(1, await CountAsync(itemId));                                  // idempotent on dedupe_key
    }

    [Fact]
    public async Task Recurring_far_edge_advances()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var itemId = await CreatePromptItemAsync(api, calId, DateTimeOffset.UtcNow.AddDays(1), rrule: "FREQ=WEEKLY");

        await RunMaterializerAsync();
        var initial = await CountAsync(itemId);
        Assert.True(initial >= 4, $"expected >=4 weekly fires in the window, got {initial}");

        await Sweep().SweepAsync(DateTimeOffset.UtcNow.AddDays(30), CancellationToken.None);
        Assert.True(await CountAsync(itemId) > initial, "sweep should extend the far edge");
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
