using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Batch create (idempotent on SourceKey, parent-by-SourceKey, partial failure) and the slim set_participants.</summary>
public sealed class BatchAndParticipantsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CreateCalendarItemRequest Item(Guid calId, string title, string sourceKey, string? parentSourceKey = null, string category = "General") => new()
    {
        CalendarId = calId, Title = title, SourceKey = sourceKey, ParentSourceKey = parentSourceKey,
        Category = category, IsAllDay = false,
        StartsAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
        EndsAt = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero), StartTimezone = "UTC",
    };

    [Fact]
    public async Task Batch_is_idempotent_on_source_key()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var body = new CreateCalendarItemsBatchRequest { Items = [Item(calId, "A", "k:a"), Item(calId, "B", "k:b")] };

        var first = (await (await api.PostAsJsonAsync("/items/batch", body)).Content.ReadFromJsonAsync<List<ItemBatchResult>>())!;
        Assert.All(first, r => Assert.Equal("created", r.Status));
        var ids = first.Select(r => r.ItemId).ToList();

        var second = (await (await api.PostAsJsonAsync("/items/batch", body)).Content.ReadFromJsonAsync<List<ItemBatchResult>>())!;
        Assert.All(second, r => Assert.Equal("existed", r.Status));
        Assert.Equal(ids, second.Select(r => r.ItemId).ToList());   // same ids on replay — no duplicates
    }

    [Fact]
    public async Task Batch_resolves_parent_by_source_key_regardless_of_order()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        // Child listed BEFORE its parent — the server topologically orders parents first.
        var body = new CreateCalendarItemsBatchRequest
        {
            Items =
            [
                Item(calId, "Leg", "k:child", parentSourceKey: "k:trip"),
                Item(calId, "Trip", "k:trip", category: "Trip"),
            ],
        };
        var res = (await (await api.PostAsJsonAsync("/items/batch", body)).Content.ReadFromJsonAsync<List<ItemBatchResult>>())!;
        Assert.All(res, r => Assert.Equal("created", r.Status));

        var parentId = res.First(r => r.SourceKey == "k:trip").ItemId;
        var childId = res.First(r => r.SourceKey == "k:child").ItemId!.Value;
        var child = await api.GetFromJsonAsync<CalendarItemDto>($"/items/{childId}", Json);
        Assert.Equal(parentId, child!.ParentItemId);
    }

    [Fact]
    public async Task Batch_one_invalid_item_does_not_fail_the_batch()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var body = new CreateCalendarItemsBatchRequest
        {
            Items = [Item(calId, "Good", "k:good"), Item(calId, "Bad", "k:bad", category: "Nonsense")],
        };
        var resp = await api.PostAsJsonAsync("/items/batch", body);
        resp.EnsureSuccessStatusCode();
        var res = (await resp.Content.ReadFromJsonAsync<List<ItemBatchResult>>())!;

        Assert.Equal("created", res.First(r => r.SourceKey == "k:good").Status);
        var bad = res.First(r => r.SourceKey == "k:bad");
        Assert.Equal("invalid", bad.Status);
        Assert.Null(bad.ItemId);
        Assert.NotNull(bad.Error);
    }

    [Fact]
    public async Task Set_participants_adds_marks_attended_and_is_add_only()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = (await (await api.PostAsJsonAsync("/items", Item(calId, "Party", "k:party"))).Content.ReadFromJsonAsync<CalendarItemDto>(Json))!;
        Guid c1 = Guid.NewGuid(), c2 = Guid.NewGuid();

        var set = (await (await api.PutAsJsonAsync($"/items/{item.Id}/participants",
            new SetParticipantsRequest { ContactIds = [c1, c2], Attended = true })).Content.ReadFromJsonAsync<SetParticipantsResult>())!;
        Assert.Equal(2, set.Added.Count);
        Assert.Equal(0, set.AlreadyPresent);

        var got = await api.GetFromJsonAsync<CalendarItemDto>($"/items/{item.Id}", Json);
        Assert.Equal(2, got!.Attendees.Count);
        Assert.All(got.Attendees, a => Assert.NotNull(a.AttendedAt));   // marked attended, not a pending invite

        // Re-adding c1 plus a new c3 → c1 already present, only c3 added.
        var set2 = (await (await api.PutAsJsonAsync($"/items/{item.Id}/participants",
            new SetParticipantsRequest { ContactIds = [c1, Guid.NewGuid()] })).Content.ReadFromJsonAsync<SetParticipantsResult>())!;
        Assert.Single(set2.Added);
        Assert.Equal(1, set2.AlreadyPresent);
    }
}
