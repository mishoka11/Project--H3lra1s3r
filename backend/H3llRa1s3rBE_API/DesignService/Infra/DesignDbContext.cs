using Microsoft.EntityFrameworkCore;
using static H3lRa1s3r.Api.DesignService.Models;

namespace DesignService.Infra
{
    public class DesignDbContext : DbContext
    {
        public DesignDbContext(DbContextOptions<DesignDbContext> options) : base(options) { }
        public DbSet<Design> Designs => Set<Design>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Design>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.UserId).HasMaxLength(64);
                e.Property(x => x.Name).HasMaxLength(128);
            });
        }
    }
}
