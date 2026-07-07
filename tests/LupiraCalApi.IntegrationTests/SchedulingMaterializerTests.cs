using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Scheduling;
using Marten;
using Npgsql;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class SchedulingMaterializerTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    /// <summary>Drive the async scheduled_fire projection to completion deterministically (the hosted daemon is disabled in tests).</summary>
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

    private async Task<CalendarItemDto> CreateFutureItemAsync(HttpClient api, Guid calId)
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
    public async Task Insert_is_idempotent_on_dedupe_key()
    {
        // The real InsertSql against the real table: a second row with the SAME dedupe_key is a no-op.
        var itemId = Guid.NewGuid();
        await InsertRowAsync(new ScheduledFireRow(Guid.NewGuid(), itemId, Guid.NewGuid(), null, DateTimeOffset.UtcNow.AddDays(1), null, TimeSpan.FromHours(24), "dedupe-x"));
        await InsertRowAsync(new ScheduledFireRow(Guid.NewGuid(), itemId, Guid.NewGuid(), null, DateTimeOffset.UtcNow.AddDays(1), null, TimeSpan.FromHours(24), "dedupe-x"));

        Assert.Equal(1, await CountByItemAsync(itemId));
    }

    [Fact]
    public async Task Setting_a_prompt_materializes_a_fire_row()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateFutureItemAsync(api, calId);

        (await api.PutAsJsonAsync($"/items/{item.Id}/prompt", Prompt())).EnsureSuccessStatusCode();
        await RunMaterializerAsync();

        Assert.True(await CountByItemAsync(item.Id) >= 1);
    }

    [Fact]
    public async Task Clearing_a_prompt_drops_future_pending_rows()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateFutureItemAsync(api, calId);

        (await api.PutAsJsonAsync($"/items/{item.Id}/prompt", Prompt())).EnsureSuccessStatusCode();
        await RunMaterializerAsync();
        Assert.True(await CountFuturePendingByItemAsync(item.Id) >= 1);

        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await api.DeleteAsync($"/items/{item.Id}/prompt")).StatusCode);
        await RunMaterializerAsync();
        Assert.Equal(0, await CountFuturePendingByItemAsync(item.Id));
    }

    // ---- helpers ----

    private async Task InsertRowAsync(ScheduledFireRow r)
    {
        await using var s = Factory.Store.LightweightSession();
        s.QueueSqlCommand(ScheduledFireSchema.InsertSql,
            r.Id, r.ItemId, r.CalendarId, (object?)r.PrincipalId ?? DBNull.Value, r.OccurrenceAt,
            (object?)r.PromptRef ?? DBNull.Value, (object?)r.ExpireAfter ?? DBNull.Value, r.DedupeKey);
        await s.SaveChangesAsync();
    }

    private Task<int> CountByItemAsync(Guid itemId) =>
        ScalarAsync("select count(*) from cal.scheduled_fire where item_id = @id", itemId);

    private Task<int> CountFuturePendingByItemAsync(Guid itemId) =>
        ScalarAsync("select count(*) from cal.scheduled_fire where item_id = @id and status = 'pending' and occurrence_at > now()", itemId);

    private async Task<int> ScalarAsync(string sql, Guid itemId)
    {
        await using var conn = new NpgsqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", itemId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
