using System.Text.Json;
using Lupira.Primitives;
using Lupira.Results;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Mappers;
using LupiraCalApi.Core.Serialization;

namespace LupiraCalApi.Core.Application.Items;

/// <summary>Reads a calendar file into item drafts without saving anything.</summary>
public static class ItemDraftReader
{
    public static OpResult<List<ItemDraftDto>> Read(string calendarFile, Guid principalId, string? zone = null)
    {
        var floatingZone = TimeZoneIds.Find(zone);
        if (!string.IsNullOrWhiteSpace(zone) && floatingZone is null) return OpResult<List<ItemDraftDto>>.Invalid($"Unknown time zone '{zone}'.");

        IReadOnlyList<ParsedEvent> events;
        try
        {
            events = ICalSerializer.ParseAll(calendarFile, floatingZone);
        }
        catch (FormatException)
        {
            return OpResult<List<ItemDraftDto>>.Invalid("Not a readable calendar file.");
        }

        return OpResult<List<ItemDraftDto>>.Ok([.. events.Select(e => e.ToDraft(SourceKey(principalId, e)))]);
    }

    // Per caller because the key becomes one global item id; a missing UID falls back to the content so re-reads stay stable.
    private static string SourceKey(Guid principalId, ParsedEvent e)
    {
        var identity = string.IsNullOrWhiteSpace(e.Uid) ? ContentHash.Of(JsonSerializer.Serialize(e)) : e.Uid.Trim();
        return "import-" + ContentHash.Of($"{principalId}\n{identity}");
    }
}
