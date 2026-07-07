using LupiraCalApi.Domain;
using LupiraCalApi.Mappers;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Member-level merge of kind-details on update: a supplied member replaces that member; omitted members are kept
/// (so setting a flight's <c>Flight</c> record does not wipe its <c>Travel</c> booking ref, and vice versa).</summary>
public class KindDetailsMapperTests
{
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
