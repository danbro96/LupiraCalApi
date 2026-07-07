using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Scheduling;
using LupiraCalApi.Worker.Clients;
using LupiraCalApi.Worker.Dispatch;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>
/// The worker's claim/dispatch loop against the real table and real aggregates, with assistant-api replaced by an
/// in-process <see cref="HttpMessageHandler"/> stub (the upstream-stub convention).
/// </summary>
public sealed class FireDispatcherTests(CalApiTestFactory factory) : IntegrationTest(factory), IDisposable
{
    const string Email = "alice@x.test";

    private readonly NpgsqlDataSource _db = NpgsqlDataSource.Create(factory.ConnectionString);

    public void Dispose() => _db.Dispose();

    // ---- harness ----

    private sealed class StubAssistant : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public Func<int, HttpResponseMessage> Responder { get; set; } = _ => Accepted(duplicate: false);

        public static HttpResponseMessage Accepted(bool duplicate) => new(HttpStatusCode.Accepted)
        {
            Content = JsonContent.Create(new { inboundItemId = Guid.NewGuid(), duplicate }),
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return Responder(Bodies.Count);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private async Task<int> TickAsync(StubAssistant stub)
    {
        await using var session = Factory.Store.QuerySession();
        var client = new AssistantFireClient(
            new HttpClient(stub) { BaseAddress = new Uri("http://assistant.test/") },
            new ServiceTokenProvider(new StubHttpClientFactory(), Options.Create(new AssistantOptions())));
        var service = new FireDispatchService(_db, session, client, Options.Create(new DispatcherOptions()),
            NullLogger<FireDispatchService>.Instance);
        return await service.RunTickAsync(CancellationToken.None);
    }

    private async Task RunMaterializerAsync()
    {
        using var daemon = await Factory.Store.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync("scheduled_fire", TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    /// <summary>Create a calendar + item (+10d) with an OnStart prompt, materialize, then backdate the row so it is due.</summary>
    private async Task<Guid> CreateDueFireAsync(HttpClient api)
    {
        var calId = await CreateCalendarAsync(api);
        var start = DateTimeOffset.UtcNow.AddDays(10);
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = calId, Title = "Fire", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC",
        });
        resp.EnsureSuccessStatusCode();
        var item = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        (await api.PutAsJsonAsync($"/items/{item.Id}/prompt", new SetItemPromptRequest
        {
            Intent = PromptIntent.Monitor,
            Instruction = "check in",
            Output = OutputKind.Summary,
            Fire = new PromptFire(PromptFireKind.OnStart, null, null),
        })).EnsureSuccessStatusCode();
        await RunMaterializerAsync();
        await ExecAsync("update cal.scheduled_fire set occurrence_at = now() - interval '1 minute' where item_id = @id", item.Id);
        return item.Id;
    }

    // ---- tests ----

    [Fact]
    public async Task Due_fire_is_delivered_and_marked_done()
    {
        var api = Factory.ApiClient(Email);
        var itemId = await CreateDueFireAsync(api);
        var stub = new StubAssistant();

        await TickAsync(stub);

        var body = JsonDocument.Parse(Assert.Single(stub.Bodies)).RootElement;
        Assert.Equal(Email, body.GetProperty("principalId").GetString());
        Assert.Equal(itemId, body.GetProperty("itemId").GetGuid());
        Assert.Equal("Agenda", body.GetProperty("calendarClass").GetString());
        Assert.Equal("check in", body.GetProperty("prompt").GetProperty("instruction").GetString());
        Assert.True(body.GetProperty("action").ValueKind is JsonValueKind.Null);
        Assert.Equal(await ScalarAsync<string>("select dedupe_key from cal.scheduled_fire where item_id = @id", itemId),
            body.GetProperty("dedupeKey").GetString());
        // interval → absolute conversion: expire_after rides as occurrence_at + 24h
        Assert.Equal(body.GetProperty("occurrenceAt").GetDateTimeOffset().AddHours(24),
            body.GetProperty("expireAfter").GetDateTimeOffset());

        var row = await RowAsync(itemId);
        Assert.Equal("done", row.Status);
        Assert.NotNull(row.FiredAt);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task Future_and_leased_rows_are_not_claimed()
    {
        var future = Guid.NewGuid();
        var leased = Guid.NewGuid();
        await InsertRowAsync(future, DateTimeOffset.UtcNow.AddDays(1));
        await InsertRowAsync(leased, DateTimeOffset.UtcNow.AddMinutes(-1));
        await ExecAsync("update cal.scheduled_fire set status = 'claimed', attempts = 1, locked_until = now() + interval '60 seconds' where item_id = @id", leased);
        var stub = new StubAssistant();

        await TickAsync(stub);

        Assert.Empty(stub.Bodies);
        Assert.Equal("pending", (await RowAsync(future)).Status);
        var leasedRow = await RowAsync(leased);
        Assert.Equal("claimed", leasedRow.Status);
        Assert.Equal(1, leasedRow.Attempts);
    }

    [Fact]
    public async Task Expired_lease_is_reclaimed_and_a_gone_item_expires()
    {
        var itemId = Guid.NewGuid();   // no aggregate behind it
        await InsertRowAsync(itemId, DateTimeOffset.UtcNow.AddMinutes(-1));
        await ExecAsync("update cal.scheduled_fire set status = 'claimed', attempts = 1, locked_until = now() - interval '1 second' where item_id = @id", itemId);
        var stub = new StubAssistant();

        await TickAsync(stub);

        Assert.Empty(stub.Bodies);
        var row = await RowAsync(itemId);
        Assert.Equal("expired", row.Status);
        Assert.Equal(2, row.Attempts);                    // the reclaim burned an attempt
        Assert.Equal("item deleted/cancelled", row.LastError);
    }

    [Fact]
    public async Task Transient_failure_backs_off_then_fails_at_max_attempts()
    {
        var api = Factory.ApiClient(Email);
        var itemId = await CreateDueFireAsync(api);
        var stub = new StubAssistant { Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) };

        await TickAsync(stub);
        var row = await RowAsync(itemId);
        Assert.Equal("pending", row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.True(row.LockedUntil > DateTimeOffset.UtcNow);   // 30s backoff rides on the lease column
        Assert.Contains("500", row.LastError);

        await ExecAsync("update cal.scheduled_fire set attempts = 5, locked_until = now() - interval '1 second' where item_id = @id", itemId);
        await TickAsync(stub);
        Assert.Equal("failed", (await RowAsync(itemId)).Status);
    }

    [Fact]
    public async Task Duplicate_ack_completes_the_row()
    {
        var api = Factory.ApiClient(Email);
        var itemId = await CreateDueFireAsync(api);
        var stub = new StubAssistant { Responder = _ => StubAssistant.Accepted(duplicate: true) };

        await TickAsync(stub);

        Assert.Equal("done", (await RowAsync(itemId)).Status);
    }

    [Fact]
    public async Task Overdue_rows_expire_without_a_push()
    {
        var itemId = Guid.NewGuid();
        await InsertRowAsync(itemId, DateTimeOffset.UtcNow.AddDays(-2));   // 24h expire_after long gone
        var stub = new StubAssistant();

        await TickAsync(stub);

        Assert.Empty(stub.Bodies);
        Assert.Equal("expired", (await RowAsync(itemId)).Status);
    }

    [Fact]
    public async Task Cleared_payload_expires_the_due_row()
    {
        // Clearing a prompt only drops FUTURE pending rows — an already-due row survives and must be
        // retired at dispatch time, not delivered.
        var api = Factory.ApiClient(Email);
        var itemId = await CreateDueFireAsync(api);
        (await api.DeleteAsync($"/items/{itemId}/prompt")).EnsureSuccessStatusCode();
        var stub = new StubAssistant();

        await TickAsync(stub);

        Assert.Empty(stub.Bodies);
        var row = await RowAsync(itemId);
        Assert.Equal("expired", row.Status);
        Assert.Equal("payload cleared or disabled", row.LastError);
    }

    // ---- SQL helpers ----

    private async Task InsertRowAsync(Guid itemId, DateTimeOffset occurrenceAt)
    {
        await using var s = Factory.Store.LightweightSession();
        var dedupe = $"{itemId:N}:{occurrenceAt.UtcDateTime:O}";
        s.QueueSqlCommand(ScheduledFireSchema.InsertSql,
            DeterministicGuid.From(dedupe), itemId, Guid.NewGuid(), DBNull.Value, occurrenceAt,
            DBNull.Value, TimeSpan.FromHours(24), dedupe);
        await s.SaveChangesAsync();
    }

    private sealed record Row(string Status, int Attempts, DateTimeOffset? LockedUntil, string? LastError, DateTimeOffset? FiredAt);

    private async Task<Row> RowAsync(Guid itemId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("select status, attempts, locked_until, last_error, fired_at from cal.scheduled_fire where item_id = @id", conn);
        cmd.Parameters.AddWithValue("id", itemId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "expected a scheduled_fire row");
        return new Row(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4));
    }

    private async Task ExecAsync(string sql, Guid itemId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", itemId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid itemId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", itemId);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }
}
