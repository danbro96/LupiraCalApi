using System.Text.Json.Serialization;
using LupiraCalApi.Core.Domain.CalendarItems;

namespace LupiraCalApi.Core.Domain.Shared;

/// <summary>What a calendar event is (its semantic identity). Reservation/travel/presence specifics are carried by
/// composable optionals on the item (see <see cref="ItemDetails"/>), not by this discriminator.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ItemCategory>))]
public enum ItemCategory
{
    General,
    Meeting,
    Appointment,
    Meal,
    Occasion,
    Outing,
    Trip,
    Stay,
    Activity,
    Focus,
    Chore,
}
