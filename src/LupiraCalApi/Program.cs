using Lupira.Auth.Jwt;
using Lupira.Clients.ServiceTokens;
using Lupira.Depz;
using Lupira.Hosting.Defaults;
using Lupira.Hosting.Health;
using Lupira.Hosting.LanEdge;
using Lupira.Hosting.Observability;
using Lupira.Hosting.OpenApi;
using Lupira.Hosting.Problems;
using Lupira.Identity.Marten.AspNetCore;
using Lupira.Mcp;
using Lupira.Postgres.Health;
using LupiraCalApi.Auth;
using LupiraCalApi.Clients;
using LupiraCalApi.Core.Abstractions;
using LupiraCalApi.Core.Application.Dav;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Scheduling;
using LupiraCalApi.Dav;
using LupiraCalApi.Dependencies;
using LupiraCalApi.Endpoints;
using LupiraCalApi.Handlers;
using LupiraCalApi.Mcp;
using Marten;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// --- Bounded context (data + transport-neutral services), registered from the Core class library.
// The connection string is read lazily from configuration (ConnectionStrings:Postgres) inside AddCalCore. ---
builder.Services.AddCalCore();

// --- Host-only services: identity (claims -> Core UserDirectory) + the thin REST handlers. ---
builder.Services.AddLupiraCurrentUser(o => o.StampProvenance = true);
builder.Services.AddScoped<MeHandler>();
builder.Services.AddScoped<CalendarsHandler>();
builder.Services.AddScoped<CalendarItemsHandler>();
builder.Services.AddScoped<RelationsHandler>();
builder.Services.AddScoped<HotspotsHandler>();
builder.Services.AddScoped<CurationHandler>();
builder.Services.AddScoped<ParticipationHandler>();
builder.Services.AddScoped<SyncHandler>();
builder.Services.AddScoped<DavBackendHandler>();
builder.Services.AddScoped<InternalItemsHandler>();

// --- Gazetteer: LupiraGeoApi owns place resolution/geocoding (Geo:BaseUrl). When configured, free-text locations
// resolve to a geo place id + label; otherwise Core's NullGeoResolver stores just the raw-text label. ---
builder.Services.Configure<GeoApiOptions>(builder.Configuration.GetSection(GeoApiOptions.SectionName));
var geoOptions = builder.Configuration.GetSection(GeoApiOptions.SectionName).Get<GeoApiOptions>() ?? new GeoApiOptions();
if (geoOptions.IsConfigured)
{
    builder.Services.AddHttpClient<IGeoResolver, GeoApiClient>(c =>
        c.BaseAddress = new Uri(geoOptions.BaseUrl.EndsWith('/') ? geoOptions.BaseUrl : geoOptions.BaseUrl + "/"));
}

// --- Contacts: LupiraContactApi owns contacts (Contacts:BaseUrl). When configured, attendee/detail contact ids
// validate against it; otherwise Core's NullContactResolver keeps refs unvalidated (fail-open). ---
builder.Services.Configure<ContactApiOptions>(builder.Configuration.GetSection(ContactApiOptions.SectionName));
var contactOptions = builder.Configuration.GetSection(ContactApiOptions.SectionName).Get<ContactApiOptions>() ?? new ContactApiOptions();
if (contactOptions.IsConfigured)
{
    builder.Services.AddHttpClient<IContactResolver, ContactApiClient>(c =>
        c.BaseAddress = new Uri(contactOptions.BaseUrl.EndsWith('/') ? contactOptions.BaseUrl : contactOptions.BaseUrl + "/"));
}

// --- Photos: LupiraPhotoApi owns photos (Photos:BaseUrl). When configured, hotspots weigh in the caller's photo
// density; otherwise Core's NullPhotoDensitySource keeps them events-only. ---
builder.Services.Configure<PhotoApiOptions>(builder.Configuration.GetSection(PhotoApiOptions.SectionName));
var photoOptions = builder.Configuration.GetSection(PhotoApiOptions.SectionName).Get<PhotoApiOptions>() ?? new PhotoApiOptions();
if (photoOptions.IsConfigured)
{
    builder.Services.AddHttpClient<IPhotoDensitySource, PhotoApiClient>(c =>
        c.BaseAddress = new Uri(photoOptions.BaseUrl.EndsWith('/') ? photoOptions.BaseUrl : photoOptions.BaseUrl + "/"));
}

// Outbound auth per OutboundAuthProvider. Token state lives in singletons, so the typed clients stay transient.
builder.Services.Configure<TokenExchangeOptions>(builder.Configuration.GetSection(TokenExchangeOptions.SectionName));
builder.Services.Configure<DavGatewayOptions>(builder.Configuration.GetSection(DavGatewayOptions.SectionName));
builder.Services.AddLupiraTokenEndpoint();
builder.Services.AddSingleton<OutboundAuthProvider>();

// Non-gating dependency probe (/depz): edges derive from the options above, probed on a dedicated client.
builder.Services.AddLupiraDepz(o =>
{
    builder.Configuration.GetSection(DepzOptions.SectionName).Bind(o);
    o.ServiceName = "lupira-cal-api";
    o.MeterName = "LupiraCalApi.Depz";
    o.MetricPrefix = "cal";
});
builder.Services.AddSingleton<IDependencyTargetSource>(sp => new StaticDependencyTargetSource(DependencyTargets.From(
    geoOptions, contactOptions, sp.GetRequiredService<ServiceTokenProvider>())));

// --- Auth: OIDC JWT for the REST/MCP surface (members, and the agent via its device-code grant); the
//           /dav-backend seam additionally requires the DAV gateway's client identity (azp). One identity
//           authority (Authentik). ---
builder.AddLupiraJwt();
var apiSchemes = LupiraJwtSchemes.Api(builder.Environment);

// `dotnet build` regenerates openapi/ via getdocument, which boots this Program with no real config —
// skip the guard there (and in Development, where the dev-header scheme needs no authority).
var isOpenApiBuild = Environment.GetCommandLineArgs()
    .Any(a => a.Contains("getdocument", StringComparison.OrdinalIgnoreCase));

var enforceConfig = !isOpenApiBuild && !builder.Environment.IsDevelopment();

var exchangeOptions = builder.Configuration.GetSection(TokenExchangeOptions.SectionName).Get<TokenExchangeOptions>() ?? new TokenExchangeOptions();
IOutboundHopOptions[] configuredHops = [.. new IOutboundHopOptions[] { geoOptions, contactOptions, photoOptions }.Where(h => h.IsConfigured)];
if (enforceConfig && configuredHops.Length > 0
    && (!exchangeOptions.IsConfigured || configuredHops.Any(h => string.IsNullOrWhiteSpace(h.Audience))))
    throw new InvalidOperationException("Geo/Contacts/Photos hops need an Audience and Auth:Exchange TokenUrl + ClientId + ClientSecret outside Development.");

var davGatewayClientId = builder.Configuration.GetSection(DavGatewayOptions.SectionName).Get<DavGatewayOptions>()?.ClientId;
builder.Services.AddAuthorizationBuilder()
    .AddLupiraApiPolicy(apiSchemes)
    .AddLupiraGatewayAzpPolicy(apiSchemes, davGatewayClientId)
    .AddLupiraInternalScopePolicy(apiSchemes);

builder.AddLupiraTelemetry("lupira-cal-api");

builder.Services.AddLupiraHealth().AddReadyCheck<DatabaseReadyCheck>("postgres");

builder.AddLupiraDefaults(o =>
{
    o.CaseInsensitiveProperties = true;
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.UtcDateTimeOffsets = true;
    o.ThrowOnBadRequest = true;
});

builder.Services.AddLupiraProblems(o => o.BadRequestDetail = IdempotencyKeyProblem.Detail);

builder.Services.AddOpenApi("v1", options => options.AddLupiraConventions(o =>
{
    o.Title = "Lupira Cal API";
    o.Description =
        "Calendar backend for Lupira (contacts live in LupiraContactApi). " +
        "Authenticate with a Bearer token issued by the OIDC provider (Authentik).";
    o.DropNullEnumMembers = true;
    o.DateTimeOffsetAsString = true;
}));

// MCP server for the agent, mounted at /mcp (LAN/WireGuard-only — not published through the tunnel).
builder.Services.AddLupiraMcp().WithTools<CalendarTools>();

var app = builder.Build();

// Deliberate, one-shot schema apply (used as a deploy step: `dotnet LupiraCalApi.dll --apply-schema`).
if (args.Contains("--apply-schema"))
{
    var store = app.Services.GetRequiredService<IDocumentStore>();
    await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    // The operational scheduled_fire queue is a raw relational table (not a Marten document) — created here too.
    await ScheduledFireSchema.EnsureExistsAsync(app.Configuration.GetConnectionString("Postgres") ?? CoreServiceCollectionExtensions.DefaultConnectionString);
    Console.WriteLine("Schema applied.");
    return;
}

// One-shot item projection rebuild (deploy step after an event-shape or snapshot change). A rebuild can change stored
// ETags without new events, so it also advances the DAV resync epoch: clients re-list and re-fetch what changed.
if (args.Contains("--rebuild-items"))
{
    var store = app.Services.GetRequiredService<IDocumentStore>();
    using var daemon = await store.BuildProjectionDaemonAsync();
    await daemon.RebuildProjectionAsync<CalendarItem>(CancellationToken.None);
    await DavChangeFeed.AdvanceEpochAsync(store);
    Console.WriteLine("CalendarItem projection rebuilt; DAV clients will resync.");
    return;
}

// One-shot fire repair (deploy step after a change to occurrence expansion): replaces every payload item's
// future-pending fires with a freshly expanded set.
if (args.Contains("--rematerialize-fires"))
{
    var sweep = new HorizonSweep(app.Services.GetRequiredService<IDocumentStore>(), app.Services.GetRequiredService<IFireMaterializer>(),
        app.Services.GetRequiredService<ILogger<HorizonSweep>>());
    await sweep.RematerializeAsync(DateTimeOffset.UtcNow, CancellationToken.None);
    Console.WriteLine("Scheduled fires rematerialized.");
    return;
}

// LAN-only surfaces (/mcp, /dav-backend): 404 anything arriving through the tunnel.
app.UseLanOnlySurfaces("/mcp", "/dav-backend", "/internal", "/.well-known/oauth-protected-resource");

// Behind the Cloudflare Tunnel the public host differs from the container, so honor forwarded headers.
app.UseLupiraDefaults();
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapLupiraOpenApi(o => o.Title = "Lupira Cal API");

app.MapLupiraHealth();
app.MapDepz();

// REST surface (at root), one MapXxx per resource.
app.MapLupiraPing("ApiPolicy");
app.MapMe();
app.MapCalendars();
app.MapOwners();
app.MapCalendarItems();
app.MapRelations();
app.MapHotspots();
app.MapCuration();
app.MapParticipation();
app.MapSync();

// The internal DAV-backend seam (LAN-only) the LupiraDavApi gateway consumes.
app.MapDavBackend();
app.MapInternal();

// Agent MCP transport (LAN/WireGuard-only; excluded from the Cloudflare Tunnel at the edge).
app.MapMcpResourceMetadata(app.Configuration["Auth:Oidc:Authority"]);
app.MapLupiraMcp();

app.Run();

// Exposes the implicit Program entry point to the integration test assembly (WebApplicationFactory<Program>).
public partial class Program;
