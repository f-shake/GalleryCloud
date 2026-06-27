using GalleryCloud.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<PhotoTag> PhotoTags => Set<PhotoTag>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<ScanLog> ScanLogs => Set<ScanLog>();
    public DbSet<ThumbnailCache> ThumbnailCaches => Set<ThumbnailCache>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // --- User ---
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.Username).HasMaxLength(128).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(128);
            e.Property(x => x.RootPath).HasMaxLength(1024).IsRequired();
        });

        // --- Photo ---
        modelBuilder.Entity<Photo>(e =>
        {
            e.ToTable("Photos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.UserId).HasMaxLength(32).IsRequired();
            e.Property(x => x.FilePath).HasMaxLength(1024).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(512).IsRequired();
            e.Property(x => x.FileFormat).HasMaxLength(16).IsRequired();
            e.Property(x => x.DeviceModel).HasMaxLength(256);
            e.Property(x => x.Md5Hash).HasMaxLength(32);

            // Unique per user+path for non-deleted photos
            e.HasIndex(x => new { x.UserId, x.FilePath })
                .IsUnique()
                .HasFilter("IsDeleted = 0");

            e.HasIndex(x => new { x.UserId, x.TakenAt }).IsDescending(false, true);
            e.HasIndex(x => new { x.UserId, x.FileFormat });
            e.HasIndex(x => new { x.UserId, x.Latitude, x.Longitude })
                .HasFilter("Latitude IS NOT NULL");

            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- Tag ---
        modelBuilder.Entity<Tag>(e =>
        {
            e.ToTable("Tags");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.UserId).HasMaxLength(32).IsRequired();
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Color).HasMaxLength(16);
            e.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- PhotoTag ---
        modelBuilder.Entity<PhotoTag>(e =>
        {
            e.ToTable("PhotoTags");
            e.HasKey(x => new { x.PhotoId, x.TagId });
            e.Property(x => x.PhotoId).HasMaxLength(32);
            e.Property(x => x.TagId).HasMaxLength(32);
            e.HasOne(x => x.Photo).WithMany().HasForeignKey(x => x.PhotoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tag).WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- Favorite ---
        modelBuilder.Entity<Favorite>(e =>
        {
            e.ToTable("Favorites");
            e.HasKey(x => new { x.UserId, x.PhotoId });
            e.Property(x => x.UserId).HasMaxLength(32);
            e.Property(x => x.PhotoId).HasMaxLength(32);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Photo).WithMany().HasForeignKey(x => x.PhotoId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- ScanLog ---
        modelBuilder.Entity<ScanLog>(e =>
        {
            e.ToTable("ScanLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.UserId).HasMaxLength(32).IsRequired();
            e.Property(x => x.Mode).HasMaxLength(16).IsRequired();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- ThumbnailCache ---
        modelBuilder.Entity<ThumbnailCache>(e =>
        {
            e.ToTable("ThumbnailCache");
            e.HasKey(x => new { x.PhotoId, x.Size });
            e.Property(x => x.PhotoId).HasMaxLength(32);
            e.Property(x => x.Size).HasMaxLength(16);
            e.Property(x => x.Format).HasMaxLength(8);
            e.Property(x => x.FilePath).HasMaxLength(1024).IsRequired();
            e.HasOne(x => x.Photo).WithMany().HasForeignKey(x => x.PhotoId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- SystemSetting ---
        modelBuilder.Entity<SystemSetting>(e =>
        {
            e.ToTable("SystemSettings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(256);
            e.Property(x => x.Value).IsRequired();
        });
    }
}
