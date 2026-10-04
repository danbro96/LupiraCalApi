using System.Net;
using System.Net.Http.Json;
using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Calendars;
using LupiraCalApi.Core.Dtos.Relations;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class RelationsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static async Task<Guid> CreateItemAsync(HttpClient api, Guid calId)
    {
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest { CalendarId = calId, Title = "Mtg", IsAllDay = false, StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = "UTC" });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!.Id;
    }

    private static async Task<RelationDto> LinkAsync(HttpClient api, Guid itemId, string toKind, string toRef, string relationType)
    {
        var resp = await api.PostAsJsonAsync($"/items/{itemId}/relations", new CreateRelationRequest { ToKind = toKind, ToRef = toRef, RelationType = relationType });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<RelationDto>())!;
    }

    private static Task<HttpResponseMessage> UnlinkBatchAsync(HttpClient api, Guid itemId, string toKind, string relationType, List<string> toRefs) =>
        api.PostAsJsonAsync($"/items/{itemId}/relations/batch/delete",
            new DeleteRelationsBatchRequest { ToKind = toKind, RelationType = relationType, ToRefs = toRefs });

    [Fact]
    public async Task Link_list_and_reverse_lookup()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var itemId = await CreateItemAsync(api, calId);

        var link = await api.PostAsJsonAsync($"/items/{itemId}/relations", new CreateRelationRequest { ToKind = "task", ToRef = "task-123", RelationType = "derived-from" });
        link.EnsureSuccessStatusCode();
        Assert.Equal("task-123", (await link.Content.ReadFromJsonAsync<RelationDto>())!.ToRef);

        var list = await api.GetFromJsonAsync<List<RelationDto>>($"/items/{itemId}/relations");
        Assert.Contains(list!, r => r.ToRef == "task-123");

        var reverse = await api.GetFromJsonAsync<List<CalendarItemDto>>("/relations?toKind=task&toRef=task-123");
        Assert.Contains(reverse!, i => i.Id == itemId);
    }

    [Fact]
    public async Task Edges_by_kind_map_each_reference_to_its_item()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var first = await CreateItemAsync(api, calId);
        var second = await CreateItemAsync(api, calId);
        var photoA = Guid.NewGuid().ToString();
        var photoB = Guid.NewGuid().ToString();

        foreach (var (item, photo) in new[] { (first, photoA), (second, photoB) })
        {
            var link = await api.PostAsJsonAsync($"/items/{item}/relations",
                new CreateRelationRequest { ToKind = "photo", ToRef = photo, RelationType = "depicts" });
            link.EnsureSuccessStatusCode();
        }
        var other = await api.PostAsJsonAsync($"/items/{first}/relations",
            new CreateRelationRequest { ToKind = "task", ToRef = "task-9", RelationType = "derived-from" });
        other.EnsureSuccessStatusCode();

        // One call gives the whole reference→item mapping; the item-shaped reverse lookup would need
        // one request per photo and still wouldn't say which photo matched which item.
        var edges = await api.GetFromJsonAsync<List<RelationDto>>("/relations/edges?toKind=photo");
        Assert.Equal(2, edges!.Count);
        Assert.Equal(first, edges.Single(e => e.ToRef == photoA).FromId);
        Assert.Equal(second, edges.Single(e => e.ToRef == photoB).FromId);
        Assert.DoesNotContain(edges, e => e.ToKind == "task");
    }

    [Fact]
    public async Task Edges_hide_items_the_caller_cannot_see()
    {
        var owner = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(owner);
        var itemId = await CreateItemAsync(owner, calId);
        var photo = Guid.NewGuid().ToString();
        (await owner.PostAsJsonAsync($"/items/{itemId}/relations",
            new CreateRelationRequest { ToKind = "photo", ToRef = photo, RelationType = "depicts" })).EnsureSuccessStatusCode();

        // A Relation carries no principal of its own — visibility has to come from the item.
        var stranger = Factory.ApiClient("mallory@x.test");
        Assert.Empty((await stranger.GetFromJsonAsync<List<RelationDto>>("/relations/edges?toKind=photo"))!);
    }

    [Fact]
    public async Task Link_on_a_missing_item_is_not_found()
    {
        var api = Factory.ApiClient(Email);
        var resp = await api.PostAsJsonAsync($"/items/{Guid.NewGuid()}/relations", new CreateRelationRequest { ToKind = "task", ToRef = "x", RelationType = "derived-from" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Link_with_non_object_metadata_is_bad_request()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var itemId = await CreateItemAsync(api, calId);

        var resp = await api.PostAsJsonAsync($"/items/{itemId}/relations", new { toKind = "task", toRef = "x", relationType = "derived-from", metadata = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Batch_links_are_idempotent_per_reference()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var itemId = await CreateItemAsync(api, calId);

        var first = await api.PostAsJsonAsync($"/items/{itemId}/relations/batch",
            new CreateRelationsBatchRequest { ToKind = "photo", RelationType = "depicts", ToRefs = ["p1", "p2"] });
        first.EnsureSuccessStatusCode();
        var second = await api.PostAsJsonAsync($"/items/{itemId}/relations/batch",
            new CreateRelationsBatchRequest { ToKind = "photo", RelationType = "depicts", ToRefs = ["p2", "p3", "p3"] });
        second.EnsureSuccessStatusCode();

        var edges = await second.Content.ReadFromJsonAsync<List<RelationDto>>();
        Assert.Equal(["p1", "p2", "p3"], edges!.Select(e => e.ToRef).Order());
        var all = await api.GetFromJsonAsync<List<RelationDto>>($"/items/{itemId}/relations");
        Assert.Equal(3, all!.Count(r => r.ToKind == "photo"));
    }

    [Fact]
    public async Task Batch_link_needs_write_access()
    {
        var owner = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(owner);
        var itemId = await CreateItemAsync(owner, calId);

        var stranger = Factory.ApiClient("mallory@x.test");
        var resp = await stranger.PostAsJsonAsync($"/items/{itemId}/relations/batch",
            new CreateRelationsBatchRequest { ToKind = "photo", RelationType = "depicts", ToRefs = ["p1"] });
        Assert.True(resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unlink_removes_only_that_relation()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var itemId = await CreateItemAsync(api, calId);
        var gone = await LinkAsync(api, itemId, "task", "task-1", "derived-from");
        var kept = await LinkAsync(api, itemId, "task", "task-2", "derived-from");

        var resp = await api.DeleteAsync($"/items/{itemId}/relations/{gone.Id}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var list = await api.GetFromJsonAsync<List<RelationDto>>($"/items/{itemId}/relations");
        Assert.Equal([kept.Id], list!.Select(r => r.Id));
    }

    [Fact]
    public async Task Unlink_of_another_items_relation_is_not_found()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var owning = await CreateItemAsync(api, calId);
        var other = await CreateItemAsync(api, calId);
        var rel = await LinkAsync(api, owning, "task", "task-1", "derived-from");

        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteAsync($"/items/{other}/relations/{rel.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteAsync($"/items/{owning}/relations/{Guid.NewGuid()}")).StatusCode);
        Assert.Single((await api.GetFromJsonAsync<List<RelationDto>>($"/items/{owning}/relations"))!);
    }

    [Fact]
    public async Task Unlink_on_a_missing_item_is_not_found()
    {
        var api = Factory.ApiClient(Email);
        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteAsync($"/items/{Guid.NewGuid()}/relations/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UnlinkBatchAsync(api, Guid.NewGuid(), "photo", "depicts", ["p1"])).StatusCode);
    }

    [Fact]
    public async Task Unlink_needs_write_access()
    {
        var owner = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(owner);
        var itemId = await CreateItemAsync(owner, calId);
        var rel = await LinkAsync(owner, itemId, "photo", "p1", "depicts");
        (await owner.PostAsJsonAsync($"/calendars/{calId}/owners", new GrantOwnerRequest { Email = "bob@x.test", Access = "read" })).EnsureSuccessStatusCode();

        var reader = Factory.ApiClient("bob@x.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.DeleteAsync($"/items/{itemId}/relations/{rel.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UnlinkBatchAsync(reader, itemId, "photo", "depicts", ["p1"])).StatusCode);
        Assert.Single((await owner.GetFromJsonAsync<List<RelationDto>>($"/items/{itemId}/relations"))!);
    }

    [Fact]
    public async Task Batch_unlink_removes_only_matching_edges_and_is_idempotent()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var itemId = await CreateItemAsync(api, calId);
        var otherItem = await CreateItemAsync(api, calId);
        (await api.PostAsJsonAsync($"/items/{itemId}/relations/batch",
            new CreateRelationsBatchRequest { ToKind = "photo", RelationType = "depicts", ToRefs = ["p1", "p2", "p3"] })).EnsureSuccessStatusCode();
        await LinkAsync(api, itemId, "photo", "p1", "features");
        await LinkAsync(api, itemId, "video", "p1", "depicts");
        await LinkAsync(api, otherItem, "photo", "p1", "depicts");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var resp = await UnlinkBatchAsync(api, itemId, "photo", "depicts", ["p1", "p3", "p-unlinked"]);
            Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

            var left = await api.GetFromJsonAsync<List<RelationDto>>($"/items/{itemId}/relations");
            Assert.Equal(["photo/depicts/p2", "photo/features/p1", "video/depicts/p1"],
                left!.Select(r => $"{r.ToKind}/{r.RelationType}/{r.ToRef}").Order());
        }

        Assert.Single((await api.GetFromJsonAsync<List<RelationDto>>($"/items/{otherItem}/relations"))!);
    }

    [Fact]
    public async Task Batch_unlink_over_the_limit_is_rejected()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var itemId = await CreateItemAsync(api, calId);

        var refs = Enumerable.Range(0, RelationService.BatchMax + 1).Select(i => $"p{i}").ToList();
        Assert.Equal(HttpStatusCode.BadRequest, (await UnlinkBatchAsync(api, itemId, "photo", "depicts", refs)).StatusCode);
    }
}
