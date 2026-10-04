# LupiraCalApi — repo rules

Docs: `docs/` (architecture, event-sourcing, temporal-backbone, dav-backend-contract).

## Domain vs ICS
- Core is domain-first (NodaTime, series, occurrence, time zone, wall-clock time), not DAV-standards-based. No DTSTART/TZID/VTIMEZONE/PRODID concepts in new core code.
- Fix domain behaviour in the domain, never in the serializer. DAV/ICS rendering is an adapter concern at the `/dav-backend` seam.
- Remaining ICS coupling in core (`ContentHash`, expander via `ICalSerializer`) is legacy; do not extend it.

## All-day spans
- All-day `EndDate` is the inclusive last day.
- `ICalSerializer` is hands-off. Emit writes the inclusive `EndDate` to `DTEND` (DAV clients see multi-day items a day short); parse reads exclusive `DTEND` into `EndDate` (a day too long); `SeriesLength` treats it as exclusive. The three errors cancel in internal round-trips, so DAV bytes are untrustworthy for all-day spans. Do not repair one without the others.
- ICS `DTEND` is exclusive (RFC 5545): imports seed `endDate = DTEND - 1`.

## Time zones
- Ical.Net `VTimeZone.FromDateTimeZone` emits wrong offsets for some anchors. `VTimeZoneBuilder` (NodaTime tzdb) is the replacement for DAV TZID output.
- Server-side expansion is unaffected: Ical.Net recurrence resolves IANA ids via NodaTime.

## API
- `Idempotency-Key` binds as `Guid?`. A non-GUID key returns a bare 400 that reads like payload validation.

## Event-shape changes
- One-shot in-place conversion command: rewrite stored rows into the new shape under the same event names, back up first, run once, `--rebuild-items`, then delete the command and the old-shape types.
- No permanent upcasters, no `_v2` names. Count rows carrying the old shape before converting.
