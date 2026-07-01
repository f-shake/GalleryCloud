using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GalleryCloud.Api.Data;

public class ThumbnailDbContextFactory : IDesignTimeDbContextFactory<ThumbnailDbContext>
{
    public ThumbnailDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ThumbnailDbContext>();
        optionsBuilder.UseSqlite("Data Source=App_Data/gallerycloud.db");
        return new ThumbnailDbContext(optionsBuilder.Options);
    }
}
