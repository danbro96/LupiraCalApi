using Lupira.Sync;
using LupiraCalApi.Core.Dtos.Calendars;
using LupiraCalApi.Core.Dtos.Sync;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class SyncEndpoints
{
    private const string ItemsSummary = "Paged item feed for offline mirrors. Omit since for a full sync (reset: replace the mirror; no tombstones); later pages and deltas return items the caller can read that changed past the cursor, plus ids of items deleted or no longer visible. Loop while hasMore, persisting cursor between calls.";

    public static IEndpointRouteBuilder MapSync(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sync").RequireAuthorization("ApiPolicy").WithTags("Sync");

        group.MapGet("/items", (string? since, int? limit, SyncHandler h, CancellationToken ct) => h.ItemsAsync(since, limit, ct))
            .WithName("GetSyncItems")
            .WithSummary(ItemsSummary)
            .Produces<SyncPage<ItemSyncChange>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/changes", (string? since, int? limit, SyncHandler h, CancellationToken ct) => h.ItemsAsync(since, limit, ct))
            .WithName("GetChanges")
            .WithSummary("Alias of GET /sync/items for existing clients.")
            .Produces<SyncPage<ItemSyncChange>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/calendars", (SyncHandler h, CancellationToken ct) => h.CalendarsAsync(ct))
            .WithName("GetSyncCalendars")
            .WithSummary("Snapshot of the caller's calendars: always reset with every calendar in changed and no cursor; replace the local set on each call.")
            .Produces<SyncPage<ContainerDto>>(StatusCodes.Status200OK);

        group.MapGet("/containers", (SyncHandler h, CancellationToken ct) => h.ContainersAsync(ct))
            .WithName("GetSyncContainers")
            .WithSummary("Snapshot of the caller's calendars for mirror reconciliation. Containers are plain documents with no event history (no cursor) — fetch once per sync cycle and diff locally.")
            .Produces<List<ContainerDto>>(StatusCodes.Status200OK);

        return app;
    }
}
