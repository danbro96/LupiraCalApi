using JasperFx.Events;
using LupiraCalApi.Domain;
using Marten;
using Marten.Events.Projections;

namespace LupiraCalApi.Scheduling;

/// <summary>
/// The materializer: an async-daemon projection that keeps <c>cal.scheduled_fire</c> in sync with each item's fired payload.
/// On payload set / item revise it replaces the item's future-pending rows with a freshly expanded set; on clear / delete /
/// cancel it drops them. Writes go through <c>QueueSqlCommand</c> so they commit atomically with the daemon's checkpoint.
/// </summary>
public sealed partial class ScheduledFireProjection(IFireMaterializer materializer) : EventProjection
{
    public Task Project(IEvent<ItemPromptSet> e, IDocumentOperations ops, CancellationToken ct) => RematerializeAsync(e.StreamId, ops, ct);
    public Task Project(IEvent<ItemActionSet> e, IDocumentOperations ops, CancellationToken ct) => RematerializeAsync(e.StreamId, ops, ct);
    public Task Project(IEvent<ItemRevised> e, IDocumentOperations ops, CancellationToken ct) => RematerializeAsync(e.StreamId, ops, ct);

    public void Project(IEvent<ItemPromptCleared> e, IDocumentOperations ops) => DropFuturePending(e.StreamId, ops);
    public void Project(IEvent<ItemActionCleared> e, IDocumentOperations ops) => DropFuturePending(e.StreamId, ops);
    public void Project(IEvent<ItemDeleted> e, IDocumentOperations ops) => DropFuturePending(e.StreamId, ops);
    public void Project(IEvent<ItemCancelled> e, IDocumentOperations ops) => DropFuturePending(e.StreamId, ops);

    private async Task RematerializeAsync(Guid itemId, IDocumentOperations ops, CancellationToken ct)
    {
        DropFuturePending(itemId, ops);   // queued before the inserts → executes first in the batch
        var item = await ops.LoadAsync<CalendarItem>(itemId, ct);
        if (item is null || item.DeletedAt is not null) return;

        var context = await SchedulingQueries.FireContextAsync(ops, item, ct);
        foreach (var r in materializer.Materialize(item, context, DateTimeOffset.UtcNow, SchedulingDefaults.Horizon))
            ops.QueueSqlCommand(ScheduledFireSchema.InsertSql,
                r.Id, r.ItemId, r.CalendarId, (object?)r.PrincipalId ?? DBNull.Value, r.OccurrenceAt,
                (object?)r.PromptRef ?? DBNull.Value, (object?)r.ExpireAfter ?? DBNull.Value, r.DedupeKey);
    }

    private static void DropFuturePending(Guid itemId, IDocumentOperations ops) =>
        ops.QueueSqlCommand(ScheduledFireSchema.DeleteFuturePendingSql, itemId);
}
