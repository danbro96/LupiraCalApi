using JasperFx.Events.Projections;
using Marten;
using Weasel.Core;

namespace LupiraCalApi.Domain;

/// <summary>Configures the single Marten store for the Calendar + Contacts API: event-sourced aggregates (inline
/// snapshots) + plain documents, in the <c>cal</c> schema. Enums serialize as strings. Mirrors LupiraWeb's pattern.</summary>
public static class MartenRegistrations
{
    public static StoreOptions UseLupiraCal(this StoreOptions opts)
    {
        opts.DatabaseSchemaName = "cal";
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        // Event-sourced aggregates (resource read models) — inline for read-your-write.
        opts.Projections.Snapshot<CalendarItem>(SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<Contact>(SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<ContactGroup>(SnapshotLifecycle.Inline);

        // Plain documents (collections, identity, places, cross-API edges) + the indexes the services query by.
        opts.Schema.For<Principal>().Index(x => x.AuthentikSub).Index(x => x.Email);
        opts.Schema.For<Calendar>();
        opts.Schema.For<AddressBook>();
        opts.Schema.For<CalendarOwner>().Index(x => x.PrincipalId).Index(x => x.CalendarId);
        opts.Schema.For<AddressBookOwner>().Index(x => x.PrincipalId).Index(x => x.AddressBookId);
        opts.Schema.For<Place>().Index(x => x.Name);
        opts.Schema.For<Relation>().Index(x => x.FromId);

        return opts;
    }
}
