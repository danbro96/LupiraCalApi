# LupiraCalApi

A single self-hosted **calendar and contacts** service that is the source of truth in Postgres and exposes
two complementary surfaces over the same data:

- **REST at root** — a clean REST API plus a **Model Context Protocol (MCP)** server at `/mcp`, for
  programmatic and agent use (text/time search, rich metadata, sharing). Authenticated with OIDC JWTs.
- **`/dav/*`** — **CalDAV/CardDAV**, so phones and desktops (iOS/macOS, Android via DAVx5, Thunderbird, eM
  Client) sync natively. Authenticated with HTTP Basic, verified by an LDAP bind.

Unlike off-the-shelf CalDAV servers, the REST/MCP surface offers structured search and arbitrary JSON
metadata that the DAV protocol can't express — while still syncing to any standard client over the one
database. Calendars and address books are **multi-owner**, so they can be shared.

A second host in this repo, **`lupira-cal-worker`** (`src/LupiraCalApi.Worker`, image
`danbro96/lupira-cal-worker`), dispatches due `cal.scheduled_fire` rows to assistant-api `POST /fires` with
claim leases, retry/backoff, and per-kind expiry.

Interactive API docs: **`/scalar/v1`** (Scalar UI) over the OpenAPI document at **`/openapi/v1.json`**.

## Tech stack

| | |
|---|---|
| Runtime | .NET 10 (`net10.0`), ASP.NET Core Minimal APIs |
| Store | **Marten 9.10.0** — event sourcing + document store (+ async daemon) on PostgreSQL |
| iCalendar / vCard | **Ical.Net 5.2.2**, **FolkerKinzel.VCards 8.1.3** (payloads + recurrence; the DAV protocol layer is hand-rolled) |
| MCP | **ModelContextProtocol.AspNetCore 1.4.0** |
| API docs | **Scalar.AspNetCore 2.16.5** + `Microsoft.AspNetCore.OpenApi 10.0.9` |
| Auth | `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.9` (OIDC); `System.DirectoryServices.Protocols 10.0.9` (LDAP for DAV) |
| Telemetry | OpenTelemetry 1.16.0 (OTLP exporter; traces, metrics, logs) |
| Tests | xUnit 2.9.3 + **Testcontainers.PostgreSql** (ephemeral Postgres) |

## Run locally

**Prerequisites:** the .NET 10 SDK, and Docker (for a local Postgres, and for the integration tests, which
spin up Postgres via Testcontainers).

```bash
# 1. A Postgres to point at
docker run -d --name lupira-cal-pg -e POSTGRES_USER=lupira_cal_user \
  -e POSTGRES_PASSWORD=devpw -e POSTGRES_DB=lupira_cal -p 5432:5432 postgres:17

export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=lupira_cal;Username=lupira_cal_user;Password=devpw"

# 2. Build & test
dotnet build LupiraCalApi.slnx -c Release
dotnet test  LupiraCalApi.slnx                     # unit tests only (DB-free; the .slnx excludes integration)
dotnet test  tests/LupiraCalApi.IntegrationTests   # integration suite (Testcontainers Postgres; run by path)

# 3. Apply the schema (see below), then run
dotnet run --project src/LupiraCalApi -- --apply-schema
dotnet run --project src/LupiraCalApi    # listens on http://localhost:8080
```

**Exercising the API without an identity provider.** In the `Development` environment the app accepts an
`X-Dev-User` header naming the caller's email — no OIDC needed:

```bash
curl localhost:8080/livez                                    # 200 (no auth)
curl localhost:8080/me                                   # 401 without auth
curl -H "X-Dev-User: dev@example.com" localhost:8080/me  # 200, JIT-provisions the principal
```

This header handler is registered **only** when the environment is `Development`; it does nothing in
production.

## Configuration

All configuration is environment-driven (ASP.NET `Section__Key` convention — use `:` in JSON, `__` in env
vars). Nothing host-specific is baked in.

| Variable | Purpose | Example |
|---|---|---|
| `ConnectionStrings__Postgres` | PostgreSQL connection (Marten store) — **required** | `Host=localhost;Port=5432;Database=lupira_cal;Username=lupira_cal_user;Password=…` |
| `Auth__Authority` | OIDC issuer/authority for `/api` JWT validation | `https://idp.example.com/application/o/lupira-cal/` |
| `Auth__Audience` | Expected JWT `aud` | `lupira-cal` |
| `Ldap__Uri` | LDAP server for `/dav` Basic-auth bind | `ldap://ldap.example.com:3389` |
| `Ldap__BaseDn` | Search base DN | `dc=example,dc=com` |
| `Ldap__ReaderDn` | Service-account DN used to search for the user | `cn=reader,ou=users,dc=example,dc=com` |
| `Ldap__ReaderSecret` | Service-account password | _(secret)_ |
| `Ldap__Filter` | User search filter (`{0}` = login email) | `(&(objectClass=user)(mail={0}))` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP collector URL. **Unset ⇒ telemetry export is a silent no-op** | `http://otel-collector:4318` |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | OTLP protocol | `http/protobuf` |
| `OTEL_EXPORTER_OTLP_HEADERS` | OTLP auth header(s) | `Authorization=Basic …` |

OIDC (`/api`) and LDAP-Basic (`/dav`) logins for the same person **converge on one principal** (matched by
OIDC `sub`, then email), so a user signs in to both surfaces with the same account.

## Schema

Marten owns its schema; there are **no EF migrations**. The schema is applied **deliberately**, not on boot —
run the app once with `--apply-schema` (it calls Marten's `ApplyAllConfiguredChangesToDatabaseAsync()` and
exits), then start it normally:

```bash
dotnet run --project src/LupiraCalApi -- --apply-schema
```

## API surface

All REST routes require an authenticated caller (OIDC JWT, or the dev header in `Development`); results
are scoped to the caller's accessible containers.

| Area | Routes |
|---|---|
| **Me** | `GET /me` · `POST /me/bootstrap` (idempotently seed the standard calendar set — agenda + agent-managed system calendars — plus a personal address book) |
| **Calendars** | `GET /calendars` (each with `type` + a calendar's `class`/`kind`) · `POST /calendars` (create a calendar or address book) |
| **Sharing** | `POST`/`DELETE /calendars/{id}/owners` · `POST`/`DELETE /address-books/{id}/owners` |
| **Items** | `GET /items` (text/time/tag search, recurrence-expanded; carries a derived completeness score) · `POST /items` · `GET`/`PUT`/`DELETE /items/{id}` · `POST /items/{id}/metadata` (merge JSON) · `PUT`/`DELETE /items/{id}/prompt` · `PUT`/`DELETE /items/{id}/action` (event-bound payload, server-side only) |
| **Participation** | `POST /items/{id}/participants` (invite) · `…/{participationId}/respond` · `…/attend` · `…/leave` · `DELETE …/{participationId}` |
| **Curation** | `GET /calendars/{id}/proposed` · `POST /items/{itemId}/calendars/{calId}/accept` · `POST /items/{itemId}/calendars/{calId}` · `DELETE /items/{itemId}/calendars/{calId}` |
| **Contacts** | `GET /contacts` (name search; carries a derived completeness score) · `POST /contacts` · `GET`/`PUT`/`DELETE /contacts/{id}` · `GET`/`POST /contacts/{id}/relations` (typed contact↔contact edges, resolved both directions) · `DELETE /contacts/{id}/relations/{toContactId}?kind=` |
| **Groups** | `GET`/`POST /address-books/{id}/groups` · `PUT /groups/{id}` · `POST`/`DELETE /groups/{id}/members…` · `DELETE /groups/{id}` |
| **Relations** | `POST`/`GET /items/{id}/relations` (link to an external service) · `GET /relations` (reverse lookup) |
| **DAV** | `/.well-known/caldav` · `/.well-known/carddav` (discovery) · `/dav/{**path}` (CalDAV/CardDAV, HTTP Basic) |
| **Health** | `GET /livez` (liveness) · `GET /readyz` (readiness — Postgres reachable) |

### MCP tools (`/mcp`)

The agent surface mirrors REST and is scoped to the caller's access:

`search_items` · `create_item` · `attach_metadata` · `query_contacts` · `list_calendars` ·
`bootstrap_me` · `grant_calendar_owner` · `revoke_calendar_owner` · `grant_addressbook_owner` ·
`revoke_addressbook_owner` · `link_item_to_task` · `find_items_linked_to_task` · `relate_contacts` ·
`unrelate_contacts` · `list_contact_relations`

## Docker & Compose

The repository root `Dockerfile` is a multi-stage build (SDK → ASP.NET runtime) that listens on `8080` and
installs `libldap2` (for the DAV LDAP bind). A reference Compose service is in
[`deploy/compose.yaml`](deploy/compose.yaml) and a Postgres role/grant script in
[`deploy/db/grants.sql`](deploy/db/grants.sql).

```bash
docker build -t lupira-cal-api .
```

> The hostnames, ports, and identity-provider URLs in `deploy/compose.yaml` are **overridable samples** —
> every one is wired to a `${VAR:-default}` env var. Set them for your own environment.

## CI

GitHub Actions ([`.github/workflows`](.github/workflows)) — a reusable `tests.yml` (unit always; the
Docker-backed integration suite gated on a `run_integration` input) consumed by:

- **`ci.yml`** — on every PR/branch: restore, build (`Release`), run the unit tests; the integration suite
  runs only when the PR carries the `integration` label.
- **`release.yml`** — on merge to `main` / `v*` tags: runs unit **and** integration, then builds and pushes a
  container image (tagged `latest`, `sha-<short>`, and the semantic version for tags).

## Project layout

```
src/
  LupiraCalApi.Core/        bounded context (no ASP.NET dependency)
    Domain/                 event-sourced aggregates, events, value objects, enums, Marten registration
    Application/            services + transport-neutral OpResult
    Auth/                   AccessResolver (container-scoped authorization)
    Scheduling/             cal.scheduled_fire table + materializer (Marten async daemon) + horizon sweep
    Dtos/ Mappers/ Serialization/
  LupiraCalApi/             thin web host
    Endpoints/ Handlers/    REST routes → handlers → Core services
    Http/                   OpResult → HTTP (TypedResults, RFC 7807)
    Dav/                    CalDAV/CardDAV router
    Mcp/                    MCP agent tools
    Auth/ Health/ Program.cs
tests/
  LupiraCalApi.UnitTests/         domain + application unit tests (in the .slnx; DB-free)
  LupiraCalApi.IntegrationTests/  integration tests (WebApplicationFactory + Testcontainers; run by path)
deploy/                          Dockerfile is at the repo root; compose.yaml + db/grants.sql here
docs/architecture.md             persistence, domain model, ownership, transport mapping
docs/temporal-backbone.md        design of calendar classes, event-bound payloads, scheduling/firing
```

See [docs/architecture.md](docs/architecture.md) for the persistence model, domain diagram, ownership/identity
model, and error-to-transport mapping. Deployment and day-2 operations are environment-specific and kept in
private ops notes.

## License

[MIT](LICENSE) © 2026 Daniel Broström.
