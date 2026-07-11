# Event sourcing: conventions & lifecycle

The rules the `CalendarItem` event store is built to, so it stays evolvable. Present-state; all knobs live in
`MartenRegistrations.UseLupiraCal`. See [architecture.md](architecture.md) for the store shape.

## Event schema & evolution

- **Stable names, not CLR names.** Every event type is mapped to an explicit wire name (`MapEventType<T>("…")`).
  The C# classes can be renamed or moved freely; the store keys on the mapped name. **Never re-map a name once
  events of it exist** — that orphans them. Evolve the *payload* via an upcaster instead.
- **No derived values on events.** Events carry only source-of-truth fields. Anything computable — notably the
  `ContentHash`/ETag, a pure hash of the canonical ICS — is recomputed in the snapshot (`CalendarItem.RecomputeHash`),
  never stored on the event, so a serializer fix heals every item on the next rebuild.
- **External lookups are snapshotted, not derived.** `LocationLabel` (from LupiraGeoApi) *is* stored on the event:
  it's the result of non-deterministic I/O and cannot be recomputed at replay. That is the dividing line — pure
  derivations are recomputed, I/O results are frozen onto the event.
- **Events are immutable records**, no behavior, holding only ids + value objects.

### Upcasting convention

- **Additive & removals need no upcaster.** System.Text.Json ignores unknown members on read and defaults absent
  ones, so adding a nullable field or removing a field (as the `ContentHash` removal did) just works.
- **A structural change or a new *required* field needs an upcaster.** Register it in `UseLupiraCal`:

  ```csharp
  // Worked example: an older ItemDeleted had no `At`. The upcaster supplies one when replaying legacy events.
  opts.Events.Upcast<ItemDeletedV1, ItemDeleted>(old => new ItemDeleted(old.ItemId, DateTimeOffset.UnixEpoch));
  ```

  The class form (`EventUpcaster<TOld,TNew>`) is preferred when the transform is non-trivial. `ItemDeletedV1` +
  its upcaster are exercised in `EventEvolutionTests` — the reference to copy. The transform sees only the old
  payload, not event metadata; if you need the server timestamp, keep the new field nullable instead.
- **DTO/API versioning is separate.** Request/response DTOs (`Dtos/`) and their mappers absorb API-shape changes;
  upcasters absorb event-shape changes. Never couple the two.

## Provenance (unbackfillable — captured at write time)

Marten always records each event's server `timestamp` + `sequence`. On top of that, `UseLupiraCal` enables and
`PrincipalDirectory.StampSession` sets, on the write session before any append:

| Metadata | Value |
|---|---|
| `UserName` (`LastModifiedBy`) | acting principal id |
| `CorrelationId` | OTel `TraceId` (ties events to traces in OpenObserve) |
| `CausationId` | OTel `SpanId` |
| header `actor.email` | acting principal email |
| header `source` | `api` (OIDC) or `dav` (email-only DAV resolve) |

`PrincipalDirectory` is the one point every surface (REST, MCP, DAV) funnels through, so all writes are covered
and future command methods inherit provenance automatically.

## Determinism (replay must be pure)

No wall-clock, `Guid.NewGuid`, randomness, culture-dependent formatting, or I/O inside any `Apply`. Times ride on
the event (`ItemDeleted.At`, the participation `At` fields); the command handler is free to be impure. The one
deliberate exception is `ScheduledFireProjection` (async): it materializes a *forward-looking* operational queue
relative to now and is idempotent (`unique(dedupe_key)`), so it is rebuild-safe by design, not by purity.

## Data lifecycle & privacy

- **Tenancy:** single conjoined tenant (one household = the deployment). Access is app-level via `CalendarOwner`,
  not store-level. Going multi-household later means Marten conjoined multi-tenancy (a `tenant_id` migration).
- **Erasure (GDPR):** PII lives in event bodies (`Title`, `Description`, `LocationLabel`, `Metadata`,
  `Prompt.Instruction`). The erasure path is **hard-delete the subject's streams + rebuild projections** —
  simplest at family scale; append-only purity is sacrificed only for erased subjects. (Crypto-shredding was
  considered and rejected as over-machinery for this scale.)
- **Retention:** full retention; no archival. Streams are never archived so DAV sync stays diffable.

## Operations

- **Schema DDL is explicit.** `AutoCreate.None` in prod (`UseLupiraCal`); DDL is the deploy step
  `dotnet LupiraCalApi.dll --apply-schema`. `AddCalCore` relaxes to `CreateOrUpdate` only in Development.
- **Projection rebuild:** the inline `CalendarItem` snapshot and the async `scheduled_fire` projection both
  rebuild from zero via the Marten daemon; rehearse a rebuild against a restored backup before relying on it.
- **Backups:** the event store is ordinary Postgres — covered by the platform DB backup. A restore must be tested.

## Invariants & concurrency

Business invariants are enforced at command time (access checks, enum validity, prompt/action XOR), not by
read-time filtering. Writes use `FetchForWriting` (optimistic concurrency on the stream version); DAV adds
`If-Match` ETag preconditions.
