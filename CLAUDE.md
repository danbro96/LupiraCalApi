# LupiraCalApi — repo rules

Docs: `docs/` (architecture, event-sourcing, temporal-backbone, dav-backend-contract).

## Domain vs ICS
- Core is domain-first (NodaTime, series, occurrence, time zone, wall-clock time), not DAV-standards-based. No DTSTART/TZID/VTIMEZONE/PRODID concepts in new core code.
- Fix domain behaviour in the domain, never in the serializer. DAV/ICS rendering is an adapter concern at the `/dav-backend` seam.
- Remaining ICS coupling in core (`ContentHash`, expander via `ICalSerializer`) is legacy; do not extend it.

## All-day spans
- All-day `EndDate` is the inclusive last day.
- `ICalSerializer` is the only place that knows `DTEND` is exclusive: emit writes `EndDate + 1`, parse reads `DTEND - 1` (clamped to `StartDate`), `SeriesLength` counts days inclusively.
- ICS `DTEND` is exclusive (RFC 5545): imports seed `endDate = DTEND - 1`.

## Time zones
- Ical.Net `VTimeZone.FromDateTimeZone` emits wrong offsets for some anchors. `VTimeZoneBuilder` (NodaTime tzdb) is the replacement for DAV TZID output.
- Server-side expansion is unaffected: Ical.Net recurrence resolves IANA ids via NodaTime.
- REST/MCP timed writes without a start zone get the calendar's zone, else `Items:DefaultTimezone` (Europe/Stockholm); end zone follows. Fixed-UTC calendar zones are bootstrap placeholders and skipped. All-day stays zone-less.

## API
- `Idempotency-Key` binds as `Guid?`. `ThrowOnBadRequest` routes binding failures to `ProblemExceptionHandler`; a non-GUID key returns detail "Idempotency-Key must be a GUID.".

## Event-shape changes
- One-shot in-place conversion command: rewrite stored rows into the new shape under the same event names, back up first, run once, `--rebuild-items`, then delete the command and the old-shape types.
- No permanent upcasters, no `_v2` names. Count rows carrying the old shape before converting.
