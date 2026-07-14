namespace LupiraCalApi.Dtos.CalendarItems;

/// <summary>One contact's participation across the caller's readable calendars: how many items they attend(ed)
/// and the most recent occurrence start (past or planned). A ranking signal for pickers/resolvers, not an ACL surface.</summary>
public sealed record ParticipationSummaryEntry(Guid ContactId, int Count, DateTimeOffset? LastAt);
