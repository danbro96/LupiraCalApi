using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.Shared;

/// <summary>Whether a calendar is part of the user's agenda (DAV/agenda-projected) or agent-managed system scaffolding (REST/DB-only, never DAV).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CalendarClass>))]
public enum CalendarClass
{
    Agenda,
    System,
}
