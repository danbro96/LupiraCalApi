using LupiraCalApi.Auth;
using LupiraCalApi.Dav;
using LupiraCalApi.Domain;
using LupiraCalApi.Endpoints;
using LupiraCalApi.Handlers;
using LupiraCalApi.Health;
using LupiraCalApi.Mcp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Marten;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// --- Bounded context (data + transport-neutral services), registered from the Core class library.
// The connection string is read lazily from configuration (ConnectionStrings:Postgres) inside AddCalCore. ---
builder.Services.AddCalCore();

// --- Host-only services: identity (claims -> Core UserDirectory) + the thin REST handlers. ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<MeHandler>();
builder.Services.AddScoped<CalendarsHandler>();
builder.Services.AddScoped<CalendarItemsHandler>();
builder.Services.AddScoped<ContactsHandler>();
builder.Services.AddScoped<RelationsHandler>();
builder.Services.AddScoped<CurationHandler>();
builder.Services.AddScoped<ParticipationHandler>();
builder.Services.AddScoped<ContactGroupsHandler>();

// --- Auth: OIDC JWT for /api (the agent obtains a member-scoped token via Authentik token-exchange);
//           HTTP Basic -> LDAP outpost for /dav. One identity authority (Authentik). ---
var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    })
    .AddScheme<AuthenticationSchemeOptions, DavBasicAuthHandler>(DavConstants.Scheme, _ => { });

// Development-only: allow X-Dev-User header auth so the API can be exercised without Authentik.
if (builder.Environment.IsDevelopment())
    authBuilder.AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, _ => { });

string[] apiSchemes = builder.Environment.IsDevelopment()
    ? [JwtBearerDefaults.AuthenticationScheme, DevAuthHandler.SchemeName]
    : [JwtBearerDefaults.AuthenticationScheme];

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ApiPolicy", p => p.AddAuthenticationSchemes(apiSchemes).RequireAuthenticatedUser())
    .AddPolicy("DavPolicy", p => p.AddAuthenticationSchemes(DavConstants.Scheme).RequireAuthenticatedUser());

// --- Observability: OpenTelemetry -> OpenObserve. Env-gated; the OTLP exporter reads OTEL_EXPORTER_OTLP_*
//     automatically (http/protobuf + Basic auth header set in compose). ---
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("lupira-cal-api"))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation();
        t.AddHttpClientInstrumentation();
        t.AddSource(Telemetry.ActivitySourceName);
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) t.AddOtlpExporter();
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation();
        m.AddHttpClientInstrumentation();
        m.AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) m.AddOtlpExporter();
    });

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadyCheck>("postgres", tags: ["ready"]);

builder.Services.AddOpenApi();

// MCP server for the agent, mounted at /api/mcp (LAN/WireGuard-only — not published through the tunnel).
builder.Services.AddMcpServer().WithHttpTransport().WithTools<CalendarTools>();

var app = builder.Build();

// Deliberate, one-shot schema apply (used as a deploy step: `dotnet LupiraCalApi.dll --apply-schema`).
if (args.Contains("--apply-schema"))
{
    var store = app.Services.GetRequiredService<IDocumentStore>();
    await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    return;
}

// Behind the Cloudflare Tunnel the public host differs from the container, so honor forwarded headers —
// CalDAV discovery must emit absolute https://cal.lupira.com/... hrefs. Restrict KnownProxies in prod.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
};
forwarded.KnownIPNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();   // /openapi/v1.json

// Health probes: /livez = liveness (no dependency checks); /readyz = readiness (Postgres reachable).
app.MapHealthChecks("/livez", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") })
    .DisableHttpMetrics();

// REST surface (/api), one MapXxx per resource.
app.MapMe();
app.MapCalendars();
app.MapCalendarItems();
app.MapContacts();
app.MapRelations();
app.MapCuration();
app.MapParticipation();
app.MapContactGroups();

// DAV service discovery (anonymous): clients probe these before auth, then follow to /dav/.
app.MapMethods("/.well-known/caldav", ["GET", "PROPFIND", "OPTIONS"], () => Results.Redirect("/dav/", permanent: true));
app.MapMethods("/.well-known/carddav", ["GET", "PROPFIND", "OPTIONS"], () => Results.Redirect("/dav/", permanent: true));

// Agent MCP transport (LAN/WireGuard-only; excluded from the Cloudflare Tunnel at the edge).
app.MapMcp("/api/mcp").RequireAuthorization("ApiPolicy");

// CalDAV/CardDAV catch-all (Basic auth). All HTTP verbs — including PROPFIND/REPORT/MKCALENDAR — reach
// DavRouter, which dispatches on the method. The cast picks the RequestDelegate Map overload.
app.Map("/dav/{**path}", (RequestDelegate)DavRouter.Handle).RequireAuthorization("DavPolicy");

app.Run();

// Exposes the implicit Program entry point to the integration test assembly (WebApplicationFactory<Program>).
public partial class Program;
