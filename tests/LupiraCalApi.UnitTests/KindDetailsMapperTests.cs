using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Mappers;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Kind-details request validation (populated members must match the kind; Travel needs ToPlace) and
/// member-level merge on update: a supplied member replaces that member; omitted members are kept
/// (so setting a flight's <c>Flight</c> record does not wipe its <c>Travel</c> booking ref, and vice versa).</summary>
public class KindDetailsMapperTests
{
    private static readonly FlightDetail SomeFlight = new("SK123", null, null, null, null, null);
    private static readonly TravelDetailRequest SomeTravel = new() { ToPlace = "Arlanda", BookingReference = "BR-1" };

    [Theory]
    [InlineData(ItemKind.Bill)]
    [InlineData(ItemKind.Appointment)]
    [InlineData(ItemKind.Generic)]
    public void Validate_rejects_member_not_matching_kind(ItemKind kind)
    {
        var error = KindDetailsMapper.Validate(kind, new ItemKindDetailsRequest { Flight = SomeFlight }, null);

        Assert.NotNull(error);
        Assert.Contains("KindDetails.Flight", error);
        Assert.Contains(kind.ToString(), error);
    }

    [Fact]
    public void Validate_accepts_matching_member_per_kind()
    {
        Assert.Null(KindDetailsMapper.Validate(ItemKind.Bill,
            new ItemKindDetailsRequest { Bill = new BillDetail(42m, "SEK", null, null, null) }, null));
        Assert.Null(KindDetailsMapper.Validate(ItemKind.Appointment,
            new ItemKindDetailsRequest { Appointment = new AppointmentDetailRequest { AppointmentType = "dentist" } }, null));
    }

    [Fact]
    public void Validate_travel_family_pairs_specialization_with_travel()
    {
        Assert.Null(KindDetailsMapper.Validate(ItemKind.Flight,
            new ItemKindDetailsRequest { Travel = SomeTravel, Flight = SomeFlight }, null));
        // But a foreign specialization on a travel kind is rejected.
        Assert.NotNull(KindDetailsMapper.Validate(ItemKind.Train,
            new ItemKindDetailsRequest { Travel = SomeTravel, Flight = SomeFlight }, null));
    }

    [Fact]
    public void Validate_rejects_details_without_a_matching_kind()
    {
        Assert.NotNull(KindDetailsMapper.Validate(null, new ItemKindDetailsRequest { Flight = SomeFlight }, null));
    }

    [Fact]
    public void Validate_availability_kind_uses_the_dedicated_field()
    {
        Assert.NotNull(KindDetailsMapper.Validate(ItemKind.Availability, new ItemKindDetailsRequest { Flight = SomeFlight }, null));
        // An all-null KindDetails object is a no-op, not an error; the dedicated field is fine.
        Assert.Null(KindDetailsMapper.Validate(ItemKind.Availability, new ItemKindDetailsRequest(), AvailabilityStatus.Sick));
        Assert.Null(KindDetailsMapper.Validate(ItemKind.Availability, null, AvailabilityStatus.Sick));
    }

    [Fact]
    public void Validate_rejects_availability_on_other_kinds()
    {
        var error = KindDetailsMapper.Validate(ItemKind.Bill, null, AvailabilityStatus.Sick);
        Assert.NotNull(error);
        Assert.Contains("Availability", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_requires_travel_to_place(string? toPlace)
    {
        var details = new ItemKindDetailsRequest { Travel = new TravelDetailRequest { ToPlace = toPlace, Carrier = "SAS" } };
        var error = KindDetailsMapper.Validate(ItemKind.Travel, details, null);

        Assert.NotNull(error);
        Assert.Contains("ToPlace", error);
    }

    [Fact]
    public void Merge_keeps_existing_members_the_update_omits()
    {
        var existing = new ItemKindDetails(Travel: new TravelDetail(Guid.NewGuid(), null, null, null, null, "BR-1"));
        var incoming = new ItemKindDetails(Flight: new FlightDetail("SK123", null, "A12", null, null, null));

        var merged = KindDetailsMapper.Merge(existing, incoming);

        Assert.Equal("BR-1", merged.Travel!.BookingReference);   // preserved
        Assert.Equal("SK123", merged.Flight!.FlightNumber);      // added
    }

    [Fact]
    public void Merge_supplied_member_replaces_the_same_existing_member()
    {
        var existing = new ItemKindDetails(Flight: new FlightDetail("SK1", "1", null, null, null, null));
        var incoming = new ItemKindDetails(Flight: new FlightDetail("SK2", null, "B7", null, null, null));

        var merged = KindDetailsMapper.Merge(existing, incoming);

        Assert.Equal("SK2", merged.Flight!.FlightNumber);   // wholesale member replace
        Assert.Equal("B7", merged.Flight!.Gate);
        Assert.Null(merged.Flight!.Terminal);               // not carried over (member replaced, not field-merged)
    }

    [Fact]
    public void Merge_onto_null_existing_returns_incoming()
    {
        var incoming = new ItemKindDetails(Bill: new BillDetail(42m, "SEK", "Vattenfall", "INV-9", null));
        Assert.Same(incoming, KindDetailsMapper.Merge(null, incoming));
    }
}
