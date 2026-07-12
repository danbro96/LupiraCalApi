using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Mappers;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Composable-detail request validation (Travel applies only to a <c>Trip</c> and needs <c>ToPlace</c>;
/// Booking is unrestricted) and member-level merge on update: a supplied member replaces that member; omitted members
/// are kept (so setting a trip's <c>Travel</c> leg does not wipe its <c>Booking</c>, and vice versa).</summary>
public class ItemDetailsMapperTests
{
    private static readonly BookingDetail SomeBooking = new(null, "CN-1", "BR-1", null, null, null, null);
    private static TravelLegRequest SomeTravel() => new() { Mode = TransportMode.Flight, ToPlaceId = Guid.NewGuid(), ToPlace = "Arlanda", Carrier = "SAS" };

    [Theory]
    [InlineData(ItemCategory.General)]
    [InlineData(ItemCategory.Meal)]
    [InlineData(ItemCategory.Appointment)]
    public void Validate_rejects_travel_on_a_non_trip_category(ItemCategory category)
    {
        var error = ItemDetailsMapper.Validate(category, new ItemDetailsRequest { Travel = SomeTravel() });

        Assert.NotNull(error);
        Assert.Contains("Trip", error);
    }

    [Fact]
    public void Validate_accepts_travel_on_a_trip()
    {
        Assert.Null(ItemDetailsMapper.Validate(ItemCategory.Trip, new ItemDetailsRequest { Travel = SomeTravel() }));
    }

    [Fact]
    public void Validate_accepts_booking_on_any_category()
    {
        Assert.Null(ItemDetailsMapper.Validate(ItemCategory.Meal, new ItemDetailsRequest { Booking = SomeBooking }));
        Assert.Null(ItemDetailsMapper.Validate(ItemCategory.Outing, new ItemDetailsRequest { Booking = SomeBooking }));
        Assert.Null(ItemDetailsMapper.Validate(null, new ItemDetailsRequest { Booking = SomeBooking }));
    }

    [Fact]
    public void Validate_null_details_is_valid()
    {
        Assert.Null(ItemDetailsMapper.Validate(ItemCategory.Trip, null));
        Assert.Null(ItemDetailsMapper.Validate(null, null));
    }

    [Fact]
    public void Validate_requires_a_resolved_travel_destination_place_id()
    {
        // REST/MCP require a resolved place id; free-text ToPlace alone (or nothing) is not a destination.
        var details = new ItemDetailsRequest { Travel = new TravelLegRequest { Mode = TransportMode.Train, ToPlace = "Arlanda", Carrier = "SJ" } };
        var error = ItemDetailsMapper.Validate(ItemCategory.Trip, details);

        Assert.NotNull(error);
        Assert.Contains("ToPlaceId", error);
    }

    [Fact]
    public void Validate_rejects_free_text_from_place_without_id()
    {
        var details = new ItemDetailsRequest { Travel = new TravelLegRequest { Mode = TransportMode.Train, ToPlaceId = Guid.NewGuid(), FromPlace = "Stockholm" } };
        var error = ItemDetailsMapper.Validate(ItemCategory.Trip, details);

        Assert.NotNull(error);
        Assert.Contains("FromPlaceId", error);
    }

    [Fact]
    public void Merge_keeps_existing_members_the_update_omits()
    {
        var existing = new ItemDetails(Booking: new BookingDetail(null, "CN-1", null, null, null, null, null));
        var incoming = new ItemDetails(Travel: new TravelLeg(TransportMode.Flight, Guid.NewGuid(), null, null, null, "SAS", "SK123", null, null, null, null));

        var merged = ItemDetailsMapper.Merge(existing, incoming);

        Assert.Equal("CN-1", merged.Booking!.ConfirmationNumber);   // preserved
        Assert.Equal("SK123", merged.Travel!.ServiceNumber);        // added
    }

    [Fact]
    public void Merge_supplied_member_replaces_the_same_existing_member()
    {
        var existing = new ItemDetails(Travel: new TravelLeg(TransportMode.Flight, Guid.NewGuid(), null, null, null, "SAS", "SK1", "Gate 1", null, null, null));
        var incoming = new ItemDetails(Travel: new TravelLeg(TransportMode.Flight, Guid.NewGuid(), null, null, null, null, "SK2", null, "Gate 7", null, null));

        var merged = ItemDetailsMapper.Merge(existing, incoming);

        Assert.Equal("SK2", merged.Travel!.ServiceNumber);   // wholesale member replace
        Assert.Equal("Gate 7", merged.Travel!.ArrivalPoint);
        Assert.Null(merged.Travel!.Carrier);                 // not carried over (member replaced, not field-merged)
    }

    [Fact]
    public void Merge_onto_null_existing_returns_incoming()
    {
        var incoming = new ItemDetails(Booking: new BookingDetail(null, "CN-9", null, null, 42m, "SEK", null));
        Assert.Same(incoming, ItemDetailsMapper.Merge(null, incoming));
    }
}
