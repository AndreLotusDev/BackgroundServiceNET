using Microsoft.EntityFrameworkCore;

namespace SteamItems.Web.Data;

public class WebDbContext(DbContextOptions<WebDbContext> options) : DbContext(options)
{
    public DbSet<SteamItem> SteamItems => Set<SteamItem>();
    public DbSet<UserSelection> UserSelections => Set<UserSelection>();

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
    }
}
