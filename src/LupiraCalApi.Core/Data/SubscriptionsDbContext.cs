using System.Text;
using System.Text.Json;
using LupiraCalApi.Core.Application.Subscriptions;
using LupiraCalApi.Core.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace LupiraCalApi.Core.Data;

/// <summary>
/// The subscribed-feed mirror (EF Core), schema <c>cal_subscriptions</c>: each <see cref="CalendarSubscription"/> and the
/// <see cref="SubscribedEvent"/>s its last refresh produced. Synced read-only data — not event-sourced, beside the Marten
/// store on the same database. Applied by EF migrations in <c>--apply-schema</c>.
/// </summary>
public sealed class SubscriptionsDbContext(DbContextOptions<SubscriptionsDbContext> options) : DbContext(options)
{
    public const string Schema = "cal_subscriptions";

    public DbSet<CalendarSubscription> Subscriptions => Set<CalendarSubscription>();

    public DbSet<SubscribedEvent> Events => Set<SubscribedEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<CalendarSubscription>(e =>
        {
            e.ToTable("subscription");
            e.HasKey(s => s.Id);
            e.Property(s => s.DisplayName).HasMaxLength(100);
            e.Property(s => s.FeedUrl).HasMaxLength(SubscriptionFeedUrl.MaxLength);
            e.Property(s => s.Color).HasConversion<string>();
            e.Property(s => s.LastError).HasConversion<string>();
            e.HasIndex(s => s.CalendarId);
            e.HasIndex(s => s.NextRefreshAt);
        });

        b.Entity<SubscribedEvent>(e =>
        {
            e.ToTable("subscribed_event");
            e.HasKey(x => x.Id);
            e.Property(x => x.Series).HasColumnType("jsonb").HasConversion(
                s => JsonSerializer.Serialize(s, SubscribedSeries.Json),
                json => JsonSerializer.Deserialize<SubscribedSeries>(json, SubscribedSeries.Json)!);
            e.HasIndex(x => x.SubscriptionId);
            e.HasIndex(x => x.FirstStart);
            e.HasOne<CalendarSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        });

        // Snake case, so the claim SQL reads like the rest of the database.
        foreach (var property in b.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
            property.SetColumnName(SnakeCase(property.Name));
    }

    private static string SnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        foreach (var c in name)
        {
            if (char.IsUpper(c) && sb.Length > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
