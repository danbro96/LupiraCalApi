using Microsoft.EntityFrameworkCore;

namespace LupiraCalApi.Data;

/// <summary>
/// EF Core relational source of truth, schema <c>cal</c>. Snake_case mapping is applied via
/// <c>UseSnakeCaseNamingConvention()</c> in Program.cs. jsonb columns are stored as raw JSON strings
/// for now; richer typing + tsvector/pg_trgm search land in Phase 2.
/// </summary>
public class CalDbContext(DbContextOptions<CalDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Calendar> Calendars => Set<Calendar>();
    public DbSet<AddressBook> AddressBooks => Set<AddressBook>();
    public DbSet<CalendarShare> CalendarShares => Set<CalendarShare>();
    public DbSet<AddressBookShare> AddressBookShares => Set<AddressBookShare>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<CalendarChange> CalendarChanges => Set<CalendarChange>();
    public DbSet<ContactChange> ContactChanges => Set<ContactChange>();
    public DbSet<Relation> Relations => Set<Relation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("cal");
        b.HasPostgresExtension("pg_trgm");   // typo-tolerant fuzzy search (gin_trgm_ops indexes below)

        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => x.AuthentikSub).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();   // store lowercased (app-side); avoids the citext extension
        });

        b.Entity<Calendar>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => new { x.OwnerId, x.Slug }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AddressBook>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => new { x.OwnerId, x.Slug }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CalendarShare>(e =>
        {
            e.HasKey(x => new { x.CalendarId, x.UserId });
            e.HasOne<Calendar>().WithMany().HasForeignKey(x => x.CalendarId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AddressBookShare>(e =>
        {
            e.HasKey(x => new { x.AddressBookId, x.UserId });
            e.HasOne<AddressBook>().WithMany().HasForeignKey(x => x.AddressBookId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Event>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.Metadata).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            e.Property(x => x.Attendees).HasColumnType("jsonb");
            e.Property(x => x.RecurrenceOverrides).HasColumnType("jsonb");
            e.HasIndex(x => new { x.CalendarId, x.IcalUid }).IsUnique();
            e.HasOne<Calendar>().WithMany().HasForeignKey(x => x.CalendarId).OnDelete(DeleteBehavior.Cascade);

            // Full-text search (generated tsvector) + fuzzy title + metadata containment + tag filtering.
            e.HasGeneratedTsVectorColumn(x => x.SearchVector, "english", x => new { x.Title, x.Description, x.Location });
            e.HasIndex(x => x.SearchVector).HasMethod("gin");
            e.HasIndex(x => x.Title).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(x => x.Metadata).HasMethod("gin").HasOperators("jsonb_path_ops");
            e.HasIndex(x => x.Tags).HasMethod("gin");
        });

        b.Entity<Contact>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.Metadata).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            e.Property(x => x.Emails).HasColumnType("jsonb");
            e.Property(x => x.Phones).HasColumnType("jsonb");
            e.Property(x => x.Addresses).HasColumnType("jsonb");
            e.HasIndex(x => new { x.AddressBookId, x.VcardUid }).IsUnique();
            e.HasOne<AddressBook>().WithMany().HasForeignKey(x => x.AddressBookId).OnDelete(DeleteBehavior.Cascade);

            e.HasGeneratedTsVectorColumn(x => x.SearchVector, "simple", x => new { x.FullName, x.Organization });
            e.HasIndex(x => x.SearchVector).HasMethod("gin");
            e.HasIndex(x => x.FullName).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(x => x.Metadata).HasMethod("gin").HasOperators("jsonb_path_ops");
            e.HasIndex(x => x.Tags).HasMethod("gin");
        });

        b.Entity<CalendarChange>(e =>
        {
            e.HasKey(x => new { x.CalendarId, x.Revision });
            e.HasOne<Calendar>().WithMany().HasForeignKey(x => x.CalendarId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ContactChange>(e =>
        {
            e.HasKey(x => new { x.AddressBookId, x.Revision });
            e.HasOne<AddressBook>().WithMany().HasForeignKey(x => x.AddressBookId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Relation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Metadata).HasColumnType("jsonb");
            e.HasIndex(x => new { x.FromKind, x.FromId });
        });
    }
}
