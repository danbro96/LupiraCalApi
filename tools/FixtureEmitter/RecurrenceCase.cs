internal sealed record RecurrenceCase(
    string Name, string Rule, DateTimeOffset Start, int DurationMinutes,
    DateTimeOffset WindowStart, DateTimeOffset WindowEnd, IReadOnlyList<DateTimeOffset> Expected, string? TimeZone = null);
