using Lupira.Sync.Marten;

namespace LupiraCalApi.Core.Application.Sync;

/// <summary>Binds <c>Sync</c>. <see cref="SettleLag"/> is how old an event must be before the feed counts it, so a
/// late-committing write is never skipped.</summary>
public sealed class SyncFeedOptions
{
    public const string SectionName = "Sync";

    public TimeSpan SettleLag { get; set; } = EventLogExtensions.DefaultSettleLag;
}
