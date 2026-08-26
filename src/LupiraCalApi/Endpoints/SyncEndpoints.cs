using LupiraCalApi.Core.Dtos.Calendars;
using LupiraCalApi.Core.Dtos.Sync;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSync(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sync").RequireAuthorization("ApiPolicy").WithTags("Sync");

        group.MapGet("/changes", (string? since, int? limit, SyncHandler h, CancellationToken ct) => h.ChangesAsync(since, limit, ct))
            .WithName("GetChanges")
            .WithSummary("Delta feed for offline mirrors: everything the caller can read that changed past the cursor, plus tombstone ids for items deleted or no longer visible. Omit since for a full sync (tombstones suppressed — replace the mirror wholesale); loop while hasMore, persisting cursor between calls.")
            .Produces<SyncChangesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/containers", (SyncHandler h, CancellationToken ct) => h.ContainersAsync(ct))
            .WithName("GetSyncContainers")
            .WithSummary("Snapshot of the caller's calendars for mirror reconciliation. Containers are plain documents with no event history (no cursor) — fetch once per sync cycle and diff locally.")
            .Produces<List<ContainerDto>>(StatusCodes.Status200OK);

        return app;
    }
}
