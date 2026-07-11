using JasperFx;
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

        // Never mutate schema at runtime; DDL is a deliberate deploy step (`--apply-schema`). AddCalCore relaxes
        // this to CreateOrUpdate in Development so `dotnet run` and the integration tests self-provision.
        opts.AutoCreateSchemaObjects = AutoCreate.None;

        // Provenance stamped on every event — unbackfillable, so captured at write time (see PrincipalDirectory).
        // Server timestamp + sequence are always recorded by Marten; these add actor, trace correlation, and headers.
        opts.Events.MetadataConfig.CorrelationIdEnabled = true;   // = OTel TraceId
        opts.Events.MetadataConfig.CausationIdEnabled = true;     // = OTel SpanId
        opts.Events.MetadataConfig.HeadersEnabled = true;         // actor.email, source (api/dav)
        opts.Events.MetadataConfig.UserNameEnabled = true;        // acting principal id

        // Stable, explicit event names decoupled from CLR type names, so the classes can be renamed/moved freely.
        // Never change a mapping once events of it exist in a live store (it would orphan them); evolve payloads via
        // upcasters instead — see docs/event-sourcing.md.
        opts.Events.MapEventType<ItemScheduled>("item-scheduled");
        opts.Events.MapEventType<ItemImported>("item-imported");
        opts.Events.MapEventType<ItemRevised>("item-revised");
        opts.Events.MapEventType<ItemCancelled>("item-cancelled");
        opts.Events.MapEventType<ItemDeleted>("item-deleted");
        opts.Events.MapEventType<ItemRestored>("item-restored");
        opts.Events.MapEventType<ItemMetadataAttached>("item-metadata-attached");
        opts.Events.MapEventType<ItemPromptSet>("item-prompt-set");
        opts.Events.MapEventType<ItemPromptCleared>("item-prompt-cleared");
        opts.Events.MapEventType<ItemActionSet>("item-action-set");
        opts.Events.MapEventType<ItemActionCleared>("item-action-cleared");
        opts.Events.MapEventType<AttendeeInvited>("attendee-invited");
        opts.Events.MapEventType<InvitationResponded>("invitation-responded");
        opts.Events.MapEventType<AttendanceConfirmed>("attendance-confirmed");
        opts.Events.MapEventType<ParticipantLeft>("participant-left");
        opts.Events.MapEventType<AttendeeRemoved>("attendee-removed");
        opts.Events.MapEventType<AddedToCalendar>("added-to-calendar");
        opts.Events.MapEventType<CalendarEntryStatusChanged>("calendar-entry-status-changed");
        opts.Events.MapEventType<RemovedFromCalendar>("removed-from-calendar");

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
