using System.Net;
using System.Net.Http.Json;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Internal;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>The place-reference check seam for geo's orphan sweep: live/deleted counts per requested place id
/// across item locations and travel legs; unrequested and zero-ref ids are absent.</summary>
public sealed class InternalPlaceReferencesTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";

    private static async Task<ItemPlaceReferencesResponse> CheckAsync(HttpClient svc, params Guid[] placeIds)
    {
        var resp = await svc.PostAsJsonAsync("/internal/items/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [.. placeIds] });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ItemPlaceReferencesResponse>())!;
    }

    private static CreateCalendarItemRequest Item(Guid calId, string title, Guid? placeId = null) => new()
    {
        CalendarId = calId,
        Title = title,
        IsAllDay = false,
        StartsAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
        EndsAt = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
        StartTimezone = "UTC",
        Location = placeId is null ? null : "Somewhere",
        PlaceId = placeId,
    };

    [Fact]
    public async Task Counts_locations_and_travel_legs_split_by_deletion()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var locPlace = Guid.NewGuid();
        var toPlace = Guid.NewGuid();
        var fromPlace = Guid.NewGuid();

        (await api.PostAsJsonAsync("/items", Item(calId, "Meeting", locPlace))).EnsureSuccessStatusCode();

        var trip = Item(calId, "Trip north");
        trip.Category = "Trip";
        trip.Details = new ItemDetailsRequest
        {
            Travel = new TravelLegRequest { Mode = TransportMode.Car, ToPlaceId = toPlace, FromPlaceId = fromPlace },
        };
        (await api.PostAsJsonAsync("/items", trip)).EnsureSuccessStatusCode();

        var deletedResp = await api.PostAsJsonAsync("/items", Item(calId, "Cancelled", locPlace));
        deletedResp.EnsureSuccessStatusCode();
        var deleted = (await deletedResp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        (await api.DeleteAsync($"/items/{deleted.Id}")).EnsureSuccessStatusCode();

        var result = await CheckAsync(Factory.ServiceClient(), locPlace, toPlace, fromPlace, Guid.NewGuid());

        var byId = result.Places.ToDictionary(p => p.PlaceId);
        Assert.Equal((1, 1), (byId[locPlace].LiveCount, byId[locPlace].DeletedCount));
        Assert.Equal((1, 0), (byId[toPlace].LiveCount, byId[toPlace].DeletedCount));
        Assert.Equal((1, 0), (byId[fromPlace].LiveCount, byId[fromPlace].DeletedCount));
        Assert.Equal(3, byId.Count);   // zero-ref requested id omitted
    }

    [Fact]
    public async Task Caps_the_id_batch_and_rejects_empty()
    {
        var svc = Factory.ServiceClient();
        var empty = await svc.PostAsJsonAsync("/internal/items/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [] });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var oversize = await svc.PostAsJsonAsync("/internal/items/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [.. Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid())] });
        Assert.Equal(HttpStatusCode.BadRequest, oversize.StatusCode);
    }

    [Fact]
    public async Task Requires_the_internal_scope()
    {
        var anon = await Factory.AnonymousClient().PostAsJsonAsync("/internal/items/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [Guid.NewGuid()] });
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

        var user = await Factory.ApiClient(Email).PostAsJsonAsync("/internal/items/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [Guid.NewGuid()] });
        Assert.Equal(HttpStatusCode.Forbidden, user.StatusCode);
    }

    [Fact]
    public async Task Tunnelled_requests_are_hidden()
    {
        var svc = Factory.ServiceClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/internal/items/place-references:check")
        {
            Content = JsonContent.Create(new CheckPlaceReferencesRequest { PlaceIds = [Guid.NewGuid()] }),
        };
        req.Headers.Add("CF-Ray", "8a1b2c3d4e5f6789-ARN");
        Assert.Equal(HttpStatusCode.NotFound, (await svc.SendAsync(req)).StatusCode);
    }
}
