using System.Text.Json.Serialization;

namespace LupiraCalApi.Dav;

[JsonConverter(typeof(JsonStringEnumConverter<DavCollectionKind>))]
public enum DavCollectionKind
{
    EventCalendar,
    TodoList,
    AddressBook,
}
