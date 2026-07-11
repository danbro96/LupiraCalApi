using LupiraCalApi.Domain;
using Marten.Services.Json.Transformations;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>
/// The reference worked example for the upcasting convention (docs/event-sourcing.md): evolving an event whose
/// payload gained a required field. <see cref="ItemDeleted"/> once had no <c>At</c>; this shows how a legacy stream
/// would be upcast on replay. Kept as a compiling, tested template — not registered, since no such legacy events exist.
/// </summary>
public class EventEvolutionTests
{
    /// <summary>The pre-<c>At</c> shape of <see cref="ItemDeleted"/>.</summary>
    public sealed record ItemDeletedV1(Guid ItemId);

    /// <summary>Class-form upcaster (the preferred form) — register with <c>opts.Events.Upcast&lt;ItemDeletedUpcaster&gt;()</c>.</summary>
    public sealed class ItemDeletedUpcaster : EventUpcaster<ItemDeletedV1, ItemDeleted>
    {
        protected override ItemDeleted Upcast(ItemDeletedV1 e) => Transform(e);

        // Legacy events carry no deletion time → UnixEpoch marks "unknown". (Prefer a nullable field when the value matters.)
        public static ItemDeleted Transform(ItemDeletedV1 e) => new(e.ItemId, DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Upcaster_supplies_a_value_for_the_new_required_field()
    {
        var id = Guid.NewGuid();
        var upcast = ItemDeletedUpcaster.Transform(new ItemDeletedV1(id));

        Assert.Equal(id, upcast.ItemId);
        Assert.Equal(DateTimeOffset.UnixEpoch, upcast.At);
    }
}
