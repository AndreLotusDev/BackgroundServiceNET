using Microsoft.EntityFrameworkCore;

namespace SteamItems.Web.Data;

public class WebDbContext(DbContextOptions<WebDbContext> options) : DbContext(options)
{
    public DbSet<SteamItem> SteamItems => Set<SteamItem>();
    public DbSet<UserSelection> UserSelections => Set<UserSelection>();
    public DbSet<ExportRecord> Exports => Set<ExportRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SteamItem>(item =>
        {
            item.HasKey(i => i.AppId);
            item.Property(i => i.AppId).ValueGeneratedNever();
            item.Property(i => i.Name).HasMaxLength(200);
            // Seeded through the migration, so it is inserted once and never duplicated on restart.
            item.HasData(SteamItemSeed.Items);
        });

        modelBuilder.Entity<UserSelection>(selection =>
        {
            selection.HasKey(s => new { s.UserId, s.AppId });
            selection.Property(s => s.UserId).HasMaxLength(200);
            selection.HasOne(s => s.Item).WithMany().HasForeignKey(s => s.AppId);
        });

        modelBuilder.Entity<ExportRecord>(export =>
        {
            export.ToTable("Exports");
            export.HasKey(e => e.Id);
            export.Property(e => e.UserId).HasMaxLength(200);
            export.Property(e => e.ObjectKey).HasMaxLength(500);
            export.Property(e => e.FileName).HasMaxLength(200);
            // SQLite cannot ORDER BY a DateTimeOffset, so store UTC ticks.
            export.Property(e => e.CreatedAt).HasConversion(
                v => v.UtcTicks,
                v => new DateTimeOffset(v, TimeSpan.Zero));
            export.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            export.Property(e => e.Error).HasMaxLength(1000);
            export.Property(e => e.CompletedAt).HasConversion(
                v => v.HasValue ? v.Value.UtcTicks : (long?)null,
                v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
            export.HasIndex(e => new { e.UserId, e.CreatedAt });
            // The status poller looks up the exports that are not finished.
            export.HasIndex(e => e.Status);
        });
    }
}
