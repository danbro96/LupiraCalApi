using JasperFx.Events.Projections;
using Marten;
using Weasel.Core;

namespace LupiraCalApi.Domain;

/// <summary>Configures the single Marten store for the Calendar API: event-sourced aggregates (inline
/// snapshots) + plain documents, in the <c>cal</c> schema. Enums serialize as strings. Mirrors LupiraWeb's pattern.</summary>
public static class MartenRegistrations
{
    public static StoreOptions UseLupiraCal(this StoreOptions opts)
    {
        opts.DatabaseSchemaName = "cal";
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        // Event-sourced aggregates (resource read models) — inline for read-your-write.
        opts.Projections.Snapshot<CalendarItem>(SnapshotLifecycle.Inline);

        // Plain documents (collections, identity, cross-API edges) + the indexes the services query by. Places are
        // owned by LupiraGeoApi and contacts by LupiraContactApi — items/legs reference them by bare Guid (no local doc).
        opts.Schema.For<Principal>().Index(x => x.AuthentikSub).Index(x => x.Email);
        opts.Schema.For<Calendar>();
        opts.Schema.For<CalendarOwner>().Index(x => x.PrincipalId).Index(x => x.CalendarId);
        opts.Schema.For<Relation>().Index(x => x.FromId);

        return opts;
    }
}
