namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>File many existing items into calendars in one call. Each entry is authorized independently.</summary>
public sealed class FileItemsBatchRequest
{
    public required List<FileItemRequest> Entries { get; set; }
}
