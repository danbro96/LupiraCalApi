using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>The ProposedAction kind a run is contracted to yield (assistant-api validates against it; cal-api only stores it).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OutputKind>))]
public enum OutputKind { RecordEdit, Event, Task, Message, Summary, Question, Relation, None }
