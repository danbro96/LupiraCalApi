internal sealed record LwwVector(
    string Name, DateTimeOffset OccurredAt, Guid CommandId, DateTimeOffset GuardTs, Guid GuardCmd, bool Wins);
