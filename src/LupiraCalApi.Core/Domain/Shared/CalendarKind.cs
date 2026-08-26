using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.Shared;

/// <summary>Purpose of a calendar within the standard set. <c>Group</c> covers household/family/team.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CalendarKind>))]
public enum CalendarKind { Personal, Group, Birthdays, Availability, Inbox, LlmPrompts, UserCheckIn, DevOps, FoodPlan, Generic }
