using LupiraCalApi.Core.Dtos.Hotspots;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class HotspotsEndpoints
{
    public static IEndpointRouteBuilder MapHotspots(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/hotspots").RequireAuthorization("ApiPolicy").WithTags("Hotspots");

        group.MapGet("/", (DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId, int? minDays, int? limit, HotspotsHandler h, CancellationToken ct) =>
                h.ListAsync(from, to, calendarId, minDays, limit, ct))
            .WithName("GetHotspots")
            .WithSummary("Places where your events and photos concentrate, derived at read time and ranked by active days (distinct UTC days with an event occurrence or a photo). Events come from calendars you can read (or calendarId), photos are your own. Defaults: all-time up to now, minDays 3, limit 100. A hotspot carries the LupiraGeoApi PlaceId it anchors to, else a reverse-geocoded label.")
            .Produces<List<HotspotDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
