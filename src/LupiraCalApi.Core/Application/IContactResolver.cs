namespace LupiraCalApi.Application;

/// <summary>A resolved contact reference: the id exists and is live in LupiraContactApi.</summary>
public sealed record ContactSummary(Guid ContactId, string DisplayName);

/// <summary>A contact's birthday, feeding the read-time Birthdays projection. <c>Year</c> is null when only the
/// month-day is known — the birthday still recurs yearly, just without an age.</summary>
public sealed record ContactBirthday(Guid ContactId, string DisplayName, int? Year, int Month, int Day);

/// <summary>
/// Cross-service contact resolution — LupiraContactApi owns contacts; items/attendees reference them by bare
/// Guid. Implemented over HTTP by the host; a no-op default (<see cref="NullContactResolver"/>, <c>IsConfigured=false</c>)
/// keeps the domain independent of the sibling service.
/// </summary>
public interface IContactResolver
{
    bool IsConfigured { get; }

    /// <summary>Resolve ids to live contacts. <c>null</c> = resolution unavailable (unconfigured or transport
    /// failure) — callers must not treat it as "not found"; absent ids in a non-null result are definitive.</summary>
    Task<IReadOnlyList<ContactSummary>?> ResolveAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct = default);

    /// <summary>Every live contact carrying a birthday — the source for the read-time Birthdays projection.
    /// <c>null</c> = unavailable (unconfigured or transport failure); an empty list means "none have one".</summary>
    Task<IReadOnlyList<ContactBirthday>?> BirthdaysAsync(CancellationToken ct = default);
}

public sealed class NullContactResolver : IContactResolver
{
    public bool IsConfigured => false;
    public Task<IReadOnlyList<ContactSummary>?> ResolveAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ContactSummary>?>(null);
    public Task<IReadOnlyList<ContactBirthday>?> BirthdaysAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ContactBirthday>?>(null);
}
