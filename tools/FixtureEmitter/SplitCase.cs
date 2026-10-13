internal sealed record SplitCase(
    string Name, SeriesShape Item, DateTimeOffset At, SeriesShape? Earlier, SeriesShape? Following,
    string SourceKey, Guid ContinuationId, Guid ParticipationId, Guid ContinuedParticipationId);
