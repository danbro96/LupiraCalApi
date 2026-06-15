# LupiraCalApi

Unified personal + family **calendar and contacts** service. One ASP.NET Core (.NET 10) app that is the single source of truth in Postgres and exposes **both**:

- `/api/*` — clean REST + **MCP** (`/api/mcp`) for the agent and a future web UI (Authentik OIDC; the agent gets a member-scoped token via token-exchange, RFC 8693).
- `/dav/*` — CalDAV/CardDAV so phones/desktops (iOS/macOS, Android via DAVx5, Thunderbird) sync natively (HTTP Basic → Authentik LDAP outpost).
- `/livez` + `/readyz` — health probes.

Both surfaces sit over one EF Core database (schema `cal`). Deployed as a TrueNAS Custom App at `https://cal.lupira.com` → MedelyNAS `:40880`. Supersedes the never-deployed Radicale plan.

> Ops + deployment docs live in the DevOps repo: `Websites/lupira-cal-api/` (deployment.md, operations.md). The approved architecture plan drives the phased build.

## Status: Phase 0 (scaffold)

Buildable skeleton: host wiring (OTel, auth schemes, health), the full EF Core model for the `cal` schema, and stubbed `/api` + `/dav` surfaces. **Not yet functional** — handlers return `501`. Build order:

| Phase | What |
|---|---|
| **0** | Scaffold (this) — model + host + stubs |
| **1** | REST/MCP core: EventService/ContactService, CRUD, MCP server, recurrence expansion |
| **2** | Search + metadata: tsvector + pg_trgm, JSONB metadata/tags, LupiraTasks cross-links |
| **3** | Read-only CalDAV/CardDAV (PROPFIND/REPORT/GET, etag/ctag) |
| **4** | Two-way DAV (PUT/DELETE + If-Match, dual-representation) |
| **5** | sync-collection + interop hardening (iOS/DAVx5/Thunderbird) |
| **6** | Google migration + birthdays/holidays seeding |
| **7** | Shared-calendar visibility under one DAV login |

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

### Migrations (when the model stabilizes)

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add Initial --project src/LupiraCalApi
dotnet ef database update --project src/LupiraCalApi      # or run the image with --apply-schema in prod
```

## Key dependencies

- **EF Core 10** + Npgsql + `EFCore.NamingConventions` (snake_case) — relational source of truth.
- **Ical.Net** (iCalendar payloads + recurrence) and **FolkerKinzel.VCards** (vCard) — payloads only; the DAV protocol layer is hand-rolled.
- **OpenTelemetry** → OpenObserve; **JwtBearer** for OIDC.
