namespace LupiraCalApi.Core.Application.Subscriptions;

/// <summary>Binds <c>Subscriptions</c>: how feeds are refreshed, how much of one is accepted, and how far synced
/// occurrences reach.</summary>
public sealed class SubscriptionOptions
{
    public const string SectionName = "Subscriptions";

    public TimeSpan Tick { get; set; } = TimeSpan.FromMinutes(1);

    public int BatchSize { get; set; } = 10;

    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromHours(24);

    public long MaxBytes { get; set; } = 10 * 1024 * 1024;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxRedirects { get; set; } = 3;

    public int MaxPerCalendar { get; set; } = 20;

    public TimeSpan ManualRefreshCooldown { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan SyncPast { get; set; } = TimeSpan.FromDays(365);

    public TimeSpan SyncFuture { get; set; } = TimeSpan.FromDays(365);
}
