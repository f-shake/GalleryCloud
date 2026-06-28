using GalleryCloud.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Data;

public class ThumbnailDbContext : DbContext
{
    public ThumbnailDbContext(DbContextOptions<ThumbnailDbContext> options) : base(options) { }

    public DbSet<ThumbnailCache> ThumbnailCaches => Set<ThumbnailCache>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ThumbnailCache>(e =>
        {
            e.ToTable("ThumbnailCache");
            e.HasKey(x => new { x.PhotoId, x.Size });
            e.Property(x => x.PhotoId).HasMaxLength(32);
            e.Property(x => x.Size).HasMaxLength(16);
            e.Property(x => x.Format).HasMaxLength(8);
            e.Property(x => x.Data).IsRequired();
        });
    }
}
