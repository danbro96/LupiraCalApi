using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Dtos.CalendarItems;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class ItemLocationTests
{
    private static readonly Guid Place = Guid.NewGuid();

    [Fact]
    public void Create_rejects_free_text_without_a_place()
    {
        Assert.False(CalendarItemService.LocationOnCreate(new CreateCalendarItemRequest { Location = "Cafe" }).IsOk);
    }

    [Fact]
    public void Create_keeps_unresolved_text_as_a_label_only()
    {
        var r = CalendarItemService.LocationOnCreate(new CreateCalendarItemRequest { Location = " Cafe ", LocationUnresolved = true });

        Assert.True(r.IsOk);
        Assert.Equal((null, "Cafe"), r.Value);
    }

    [Fact]
    public void Create_rejects_unresolved_with_a_place()
    {
        Assert.False(CalendarItemService.LocationOnCreate(new CreateCalendarItemRequest { Location = "Cafe", PlaceId = Place, LocationUnresolved = true }).IsOk);
    }

    [Fact]
    public void Create_with_a_place_keeps_the_label()
    {
        Assert.Equal((Place, "Cafe"), CalendarItemService.LocationOnCreate(new CreateCalendarItemRequest { Location = "Cafe", PlaceId = Place }).Value);
    }

    private static CalendarItem Anchored() => new() { PlaceId = Place, LocationLabel = "Cafe" };

    [Fact]
    public void Update_rejects_changed_free_text_without_a_place()
    {
        Assert.False(CalendarItemService.LocationOnUpdate(new UpdateCalendarItemRequest { Location = "Bar" }, Anchored()).IsOk);
    }

    [Fact]
    public void Update_rejects_changed_free_text_with_a_cleared_place()
    {
        Assert.False(CalendarItemService.LocationOnUpdate(new UpdateCalendarItemRequest { PlaceIdProvided = true, Location = "Bar" }, Anchored()).IsOk);
    }

    [Fact]
    public void Update_with_unresolved_stores_the_label_and_drops_the_place()
    {
        var r = CalendarItemService.LocationOnUpdate(new UpdateCalendarItemRequest { Location = "Bar", LocationUnresolved = true }, Anchored());

        Assert.True(r.IsOk);
        Assert.Equal((null, "Bar"), r.Value);
    }

    [Fact]
    public void Update_with_unresolved_and_no_text_clears_the_location()
    {
        Assert.Equal((null, null), CalendarItemService.LocationOnUpdate(new UpdateCalendarItemRequest { LocationUnresolved = true }, Anchored()).Value);
    }

    [Fact]
    public void Update_rejects_unresolved_with_a_place()
    {
        Assert.False(CalendarItemService.LocationOnUpdate(new UpdateCalendarItemRequest { PlaceId = Place, LocationUnresolved = true }, Anchored()).IsOk);
    }

    [Fact]
    public void Update_clearing_the_place_may_re_send_the_current_label()
    {
        Assert.Equal((null, "Cafe"), CalendarItemService.LocationOnUpdate(new UpdateCalendarItemRequest { PlaceIdProvided = true, Location = "Cafe" }, Anchored()).Value);
    }

    [Fact]
    public void Update_without_location_fields_keeps_both()
    {
        Assert.Equal((Place, "Cafe"), CalendarItemService.LocationOnUpdate(new UpdateCalendarItemRequest { Title = "x" }, Anchored()).Value);
    }
}
