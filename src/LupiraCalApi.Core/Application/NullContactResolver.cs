namespace LupiraCalApi.Core.Application;

public sealed class NullContactResolver : IContactResolver
{
    public bool IsConfigured => false;
    public Task<IReadOnlyList<ContactSummary>?> ResolveAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ContactSummary>?>(null);
    public Task<IReadOnlyList<ContactBirthday>?> BirthdaysAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ContactBirthday>?>(null);
}
