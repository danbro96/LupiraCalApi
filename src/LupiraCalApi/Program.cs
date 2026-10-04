using System.Globalization;
using System.Text.Json;
using Lupira.Auth.DevUser;
using Lupira.Depz;
using Lupira.Hosting.Defaults;
using Lupira.Hosting.Health;
using Lupira.Hosting.LanEdge;
using Lupira.Hosting.Observability;
using Lupira.Hosting.Problems;
using Lupira.Mcp;
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
using LupiraCalApi.Health;
using LupiraCalApi.Mcp;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

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
builder.Services.AddHttpClient(TokenEndpointClient.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenEndpointClient>();
builder.Services.AddSingleton<TokenCache>();
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
    geoOptions, contactOptions, sp.GetRequiredService<TokenEndpointClient>(), sp.GetRequiredService<TokenCache>())));

// --- Auth: OIDC JWT for the REST/MCP surface (members, and the agent via its device-code grant); the
//           /dav-backend seam additionally requires the DAV gateway's client identity (azp). One identity
//           authority (Authentik). ---
// `dotnet build` regenerates openapi/ via getdocument, which boots this Program with no real config —
// skip the guard there (and in Development, where the dev-header scheme needs no authority).
var isOpenApiBuild = Environment.GetCommandLineArgs()
    .Any(a => a.Contains("getdocument", StringComparison.OrdinalIgnoreCase));

var enforceConfig = !isOpenApiBuild && !builder.Environment.IsDevelopment();

var oidc = builder.Configuration.GetSection(OidcAuthOptions.SectionName).Get<OidcAuthOptions>() ?? new OidcAuthOptions();
if (enforceConfig && (string.IsNullOrWhiteSpace(oidc.Authority) || string.IsNullOrWhiteSpace(oidc.Audience)))
    throw new InvalidOperationException("Auth:Oidc Authority + Audience are required outside Development.");

var exchangeOptions = builder.Configuration.GetSection(TokenExchangeOptions.SectionName).Get<TokenExchangeOptions>() ?? new TokenExchangeOptions();
IOutboundHopOptions[] configuredHops = [.. new IOutboundHopOptions[] { geoOptions, contactOptions, photoOptions }.Where(h => h.IsConfigured)];
if (enforceConfig && configuredHops.Length > 0
    && (!exchangeOptions.IsConfigured || configuredHops.Any(h => string.IsNullOrWhiteSpace(h.Audience))))
    throw new InvalidOperationException("Geo/Contacts/Photos hops need an Audience and Auth:Exchange TokenUrl + ClientId + ClientSecret outside Development.");

var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = oidc.Authority;
        options.Audience = oidc.Audience;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.Events = new JwtBearerEvents
        {
            // MCP auth spec: a 401 on /mcp advertises the RFC 9728 metadata so clients can discover the
            // issuer. HandleResponse suppresses the default bare "Bearer" header so exactly one goes out.
            OnChallenge = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/mcp"))
                {
                    ctx.HandleResponse();
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    ctx.Response.Headers.WWWAuthenticate =
                        $"Bearer resource_metadata=\"{McpResourceMetadata.ResourceMetadataUrl(ctx.Request)}\"";
                }

                return Task.CompletedTask;
            },
        };
    });

// Development-only: allow X-Dev-User header auth so the API can be exercised without Authentik.
if (builder.Environment.IsDevelopment())
    authBuilder.AddLupiraDevHeaderAuth();

string[] apiSchemes = builder.Environment.IsDevelopment()
    ? [JwtBearerDefaults.AuthenticationScheme, DevAuthenticationBuilderExtensions.DefaultScheme]
    : [JwtBearerDefaults.AuthenticationScheme];

var davGatewayClientId = builder.Configuration.GetSection(DavGatewayOptions.SectionName).Get<DavGatewayOptions>()?.ClientId;
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ApiPolicy", p => p.AddAuthenticationSchemes(apiSchemes).RequireAuthenticatedUser())
    // The DAV gateway's service identity: a valid token for this API (aud) minted by the gateway's
    // client (azp). Dev-header auth passes in Development so tests can drive the seam directly.
    .AddPolicy("DavBackendPolicy", p => p.AddAuthenticationSchemes(apiSchemes).RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
            ctx.User.Identity?.AuthenticationType == DevAuthenticationBuilderExtensions.DefaultScheme
            || DavGatewayOptions.IsGateway(ctx.User, davGatewayClientId)))
    // internal:read is granted only to service clients — user tokens authenticate but never pass this.
    .AddPolicy("InternalPolicy", p => p.AddAuthenticationSchemes(apiSchemes).RequireAuthenticatedUser()
        .RequireAssertion(ctx => ctx.User.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains("internal:read")));

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

builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, _) =>
    {
        document.Info = new()
        {
            Title = "Lupira Cal API",
            Version = "v1",
            Description =
                "Calendar backend for Lupira (contacts live in LupiraContactApi). " +
                "Authenticate with a Bearer token issued by the OIDC provider (Authentik).",
        };
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "OIDC bearer token. Send as `Authorization: Bearer <token>`.",
        };
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.Schemas["ProblemDetails"] = ProblemDetailsSchema();
        return Task.CompletedTask;
    });
    // The UtcDateTimeOffsetConverter hides the underlying CLR type from schema inference, which would
    // otherwise emit `format: date-time` with no `type` at all.
    options.AddSchemaTransformer((schema, context, _) =>
    {
        var t = context.JsonTypeInfo.Type;
        if (t == typeof(DateTimeOffset) || t == typeof(DateTimeOffset?))
        {
            schema.Type = t == typeof(DateTimeOffset?)
                ? JsonSchemaType.String | JsonSchemaType.Null
                : JsonSchemaType.String;
            schema.Format = "date-time";
        }

        // A nullable use of an enum (ItemStatus?) makes the framework append null to the shared
        // component schema, although the property's own oneOf already carries the nullability.
        // Generators read that null onto the enum type itself, so non-nullable uses inherit it too.
        if (schema.Enum is { Count: > 0 } members)
        {
            for (var i = members.Count - 1; i >= 0; i--)
            {
                if (members[i] is null || members[i]!.GetValueKind() == JsonValueKind.Null)
                {
                    members.RemoveAt(i);
                }
            }
        }

        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requiresAuth = endpointMetadata.OfType<IAuthorizeData>().Any()
                        && !endpointMetadata.OfType<IAllowAnonymous>().Any();
        if (requiresAuth)
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = new List<string>(),
            });
            AddProblem(operation, context.Document, StatusCodes.Status401Unauthorized, "Unauthorized");
        }

        // The cross-cutting code no endpoint declares — ProblemExceptionHandler produces it.

        AddProblem(operation, context.Document, StatusCodes.Status500InternalServerError, "Internal server error");

        // Bodyless 4xx/5xx come from the non-generic arms of the typed-result unions (NotFound,

        // UnauthorizedHttpResult). UseStatusCodePages fills them at runtime, so declare the shape.

        foreach (var code in operation.Responses?.Keys.ToList() ?? [])
        {
            if (code.Length != 3 || code[0] is not ('4' or '5')) continue;

            var existing = operation.Responses![code];

            if (existing.Content is { Count: > 0 }) continue;

            operation.Responses[code] = new OpenApiResponse
            { Description = existing.Description, Content = ProblemContent(context.Document) };
        }

        return Task.CompletedTask;
    });
});

// Every error response carries the same shape, so a generated client types its error once instead of
// falling back to `void`.
static Dictionary<string, OpenApiMediaType> ProblemContent(OpenApiDocument document) =>
    new() { ["application/problem+json"] = new() { Schema = new OpenApiSchemaReference("ProblemDetails", document) } };

static void AddProblem(OpenApiOperation operation, OpenApiDocument document, int status, string description)
{
    var code = status.ToString(CultureInfo.InvariantCulture);
    operation.Responses ??= [];
    if (operation.Responses.ContainsKey(code)) return;
    operation.Responses[code] = new OpenApiResponse { Description = description, Content = ProblemContent(document) };
}

// RFC 9457. Declared here because nothing returns the CLR type directly, so the generator never emits it.
static OpenApiSchema ProblemDetailsSchema() => new()
{
    Type = JsonSchemaType.Object,
    Description = "RFC 9457 problem details.",
    Properties = new Dictionary<string, IOpenApiSchema>
    {
        ["type"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["title"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null, Format = "int32" },
        ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
    },
};

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

app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
app.MapScalarApiReference("/scalar", o => o
        .WithTitle("Lupira Cal API")
        .WithTheme(ScalarTheme.BluePlanet))
    .AllowAnonymous();

app.MapGet("/", () => TypedResults.Redirect("/scalar"))
   .ExcludeFromDescription()
   .AllowAnonymous();

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
