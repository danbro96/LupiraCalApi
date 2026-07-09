using LupiraCalApi.Application;
using LupiraCalApi.Domain;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>When LupiraGeoApi is configured, PlaceService delegates resolution to it and mirrors the authoritative
/// place (id + coordinates) into the local <c>cal.Place</c> catalog, so ICS generation and the place→items reverse
/// index need no cross-service call.</summary>
public sealed class GeoCutoverTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private sealed class StubGeo(GeoPlaceResolution res) : IGeoResolver
    {
        public bool IsConfigured => true;
        public Task<GeoPlaceResolution?> ResolveAsync(string text, CancellationToken ct = default) => Task.FromResult<GeoPlaceResolution?>(res);
    }

    [Fact]
    public async Task Resolve_mirrors_the_geo_place_id_and_coordinates()
    {
        var geoId = Guid.NewGuid();
        var stub = new StubGeo(new GeoPlaceResolution(geoId, "Cafe Central", 59.3293, 18.0686));

        using var scope = Factory.Services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var places = new PlaceService(session, stub);

        var resolved = await places.ResolveLabelAsync("cafe central");
        await session.SaveChangesAsync();

        Assert.Equal(geoId, resolved);   // returns the authoritative geo id, not a fresh local one
        var mirror = await session.LoadAsync<Place>(geoId);
        Assert.NotNull(mirror);
        Assert.Equal("Cafe Central", mirror!.Name);
        Assert.Equal(59.3293, mirror.Latitude);
        Assert.Equal(18.0686, mirror.Longitude);
    }
}
