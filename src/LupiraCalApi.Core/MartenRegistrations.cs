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
        opts.Events.MapEventType<ItemScheduled>("item_scheduled");
        opts.Events.MapEventType<ItemImported>("item_imported");
        opts.Events.MapEventType<ItemRevised>("item_revised");
        opts.Events.MapEventType<ItemCancelled>("item_cancelled");
        opts.Events.MapEventType<ItemDeleted>("item_deleted");
        opts.Events.MapEventType<ItemRestored>("item_restored");
        opts.Events.MapEventType<ItemMetadataAttached>("item_metadata_attached");
        opts.Events.MapEventType<ItemPromptSet>("item_prompt_set");
        opts.Events.MapEventType<ItemPromptCleared>("item_prompt_cleared");
        opts.Events.MapEventType<ItemActionSet>("item_action_set");
        opts.Events.MapEventType<ItemActionCleared>("item_action_cleared");
        opts.Events.MapEventType<AttendeeInvited>("attendee_invited");
        opts.Events.MapEventType<InvitationResponded>("invitation_responded");
        opts.Events.MapEventType<AttendanceConfirmed>("attendance_confirmed");
        opts.Events.MapEventType<ParticipantLeft>("participant_left");
        opts.Events.MapEventType<AttendeeRemoved>("attendee_removed");
        opts.Events.MapEventType<AddedToCalendar>("added_to_calendar");
        opts.Events.MapEventType<CalendarEntryStatusChanged>("calendar_entry_status_changed");
        opts.Events.MapEventType<RemovedFromCalendar>("removed_from_calendar");

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
