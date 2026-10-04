using Microsoft.EntityFrameworkCore;

namespace SteamItems.Worker.Data;

/// <summary>worker.db: what the Worker read from the uploaded files. Separate from the Web's web.db.</summary>
public class WorkerDbContext(DbContextOptions<WorkerDbContext> options) : DbContext(options)
{
    public DbSet<ProcessedFile> ProcessedFiles => Set<ProcessedFile>();
    public DbSet<ProcessedItem> ProcessedItems => Set<ProcessedItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedFile>(file =>
        {
            file.HasKey(f => f.Id);
            file.Property(f => f.Bucket).HasMaxLength(100);
            file.Property(f => f.Key).HasMaxLength(1024);
            file.Property(f => f.ETag).HasMaxLength(100);
            file.Property(f => f.UserId).HasMaxLength(200);
            file.Property(f => f.Status).HasConversion<string>().HasMaxLength(50);
            // SQLite cannot ORDER BY a DateTimeOffset, so store UTC ticks.
            file.Property(f => f.StartedAt).HasConversion(
                v => v.UtcTicks,
                v => new DateTimeOffset(v, TimeSpan.Zero));
            file.Property(f => f.CompletedAt).HasConversion(
                v => v.HasValue ? v.Value.UtcTicks : (long?)null,
                v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
            // File idempotency: the same object version is stored once.
            file.HasIndex(f => new { f.Bucket, f.Key, f.ETag }).IsUnique();
            file.HasMany(f => f.Items).WithOne().HasForeignKey(i => i.FileId);
        });

        modelBuilder.Entity<ProcessedItem>(item =>
        {
            item.HasKey(i => i.Id);
            item.Property(i => i.Name).HasMaxLength(200);
            item.Property(i => i.Status).HasConversion<string>().HasMaxLength(50);
            item.Property(i => i.Error).HasMaxLength(500);
            item.Property(i => i.ProcessedAt).HasConversion(
                v => v.UtcTicks,
                v => new DateTimeOffset(v, TimeSpan.Zero));
            // Row idempotency: a resumed or duplicated file cannot store a row twice.
            item.HasIndex(i => new { i.FileId, i.RowNumber }).IsUnique();
        });
    }
}
