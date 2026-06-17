# LupiraCalApi

Unified personal + family **calendar and contacts** service. One ASP.NET Core (.NET 10) app that is the single source of truth in Postgres and exposes **both**:

- `/api/*` — clean REST + **MCP** (`/api/mcp`) for the agent and a future web UI (Authentik OIDC; the agent gets a member-scoped token via token-exchange, RFC 8693).
- `/dav/*` — CalDAV/CardDAV so phones/desktops (iOS/macOS, Android via DAVx5, Thunderbird) sync natively (HTTP Basic → Authentik LDAP outpost).
- `/livez` + `/readyz` — health probes.

Both surfaces sit over one **Marten event-sourced store** in Postgres (schema `cal`). Deployed as a TrueNAS Custom App at `https://cal.lupira.com` → MedelyNAS `:40880`. Supersedes the never-deployed Radicale plan.

> **Architecture & data model:** see [docs/data-model.md](docs/data-model.md) — the agreed boundaries, class diagrams, and event-sourcing shape.
>
> Ops + deployment docs live in the DevOps repo: `Websites/lupira-cal-api/` (deployment.md, operations.md).

## Status: event-sourced rebuild (in progress)

Converting from the original EF Core scaffold to the all-Marten, event-sourced model in [docs/data-model.md](docs/data-model.md): `CalendarItem`/`Contact`/`ContactGroup` aggregates, projection read models, derived `*ChangeFeed` sync (token = Marten event `Sequence`), many-to-many `CalendarItem`↔`Calendar` with `proposed`/`accepted` curation, hierarchical `Place`, first-class `Participation`, and table-per-type item kinds. The CalDAV/CardDAV contract is preserved throughout.

## Layout

```
src/LupiraCalApi/
  Program.cs            host: config, OTel, auth, health, route groups
  Data/                 CalDbContext + entities (schema `cal`, snake_case)
  Api/                  REST + MCP endpoints (/api)
  Dav/                  CalDAV/CardDAV catch-all router (/dav)
  Auth/                 DavBasicAuthHandler (Basic -> LDAP)
  Domain/               RecurrenceExpander, Telemetry (services land in Phase 1)
  Serialization/        iCalendar / vCard mappers (Ical.Net, FolkerKinzel.VCards)
  Health/               DatabaseReadyCheck (/readyz)
deploy/                 compose.yaml + db/grants.sql (TrueNAS Custom App + DB provisioning)
```

## Develop

```bash
# Needs a local Postgres reachable via ConnectionStrings:Postgres (defaults to localhost lupira_cal).
dotnet run --project src/LupiraCalApi

curl -s localhost:8080/livez          # 200
curl -s localhost:8080/openapi/v1.json | head
curl -s -o /dev/null -w "%{http_code}\n" localhost:8080/api/me   # 401 without a token
```

### Schema

Marten manages the `cal` schema (event tables + projection/document tables). No EF migrations — apply the configured schema directly:

```bash
dotnet run --project src/LupiraCalApi -- --apply-schema    # ApplyAllConfiguredChangesToDatabase
```

## Key dependencies

- **Marten** (event store + document store on Postgres) — `CalendarItem`/`Contact`/`ContactGroup` are event-sourced aggregates; collections + projections are documents. Schema apply via `--apply-schema` (`ApplyAllConfiguredChangesToDatabase`), no EF migrations.
- **Ical.Net** (iCalendar payloads + recurrence) and **FolkerKinzel.VCards** (vCard) — payloads only; the DAV protocol layer is hand-rolled.
- **OpenTelemetry** → OpenObserve; **JwtBearer** for OIDC.
