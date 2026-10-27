using System.Text.Json;
using System.Text.Json.Serialization;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.Subscriptions;

/// <summary>What a subscribed event says about itself and when it happens, in the same shape a stored item's series
/// has, so the one <see cref="RecurrenceExpander"/> expands both.</summary>
public sealed class SubscribedSeries
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public required string Title { get; set; }

    public string? Description { get; set; }

    public string? LocationLabel { get; set; }

    public ItemStatus Status { get; set; }

    public bool IsAllDay { get; set; }

    public DateTimeOffset? StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public string? StartTimezone { get; set; }

    public string? EndTimezone { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public string? RecurrenceRule { get; set; }

    public DateTimeOffset[]? ExcludedOccurrences { get; set; }

    public DateTimeOffset[]? ExtraOccurrences { get; set; }

    public OccurrenceOverride[]? OccurrenceOverrides { get; set; }

    [JsonIgnore]
    public bool IsRecurring => !string.IsNullOrWhiteSpace(RecurrenceRule);

    internal CalendarItem Schedule() => new()
    {
        IsAllDay = IsAllDay,
        StartsAt = StartsAt,
        EndsAt = EndsAt,
        StartTimezone = StartTimezone,
        StartDate = StartDate,
        EndDate = EndDate,
        RecurrenceRule = RecurrenceRule,
        ExcludedOccurrences = ExcludedOccurrences,
        ExtraOccurrences = ExtraOccurrences,
        OccurrenceOverrides = OccurrenceOverrides,
    };
}
