using Microsoft.EntityFrameworkCore;
using H3lRa1s3r.Api.CatalogService.Models;

namespace H3lRa1s3r.Api.CatalogService.Infra
{
    public class CatalogDbContext : DbContext
    {
        public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
            : base(options)
        {
        }

        public DbSet<Product> Products => Set<Product>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasKey(p => p.Id);

                entity.Property(p => p.Id).IsRequired();

                entity.Property(p => p.Name)
                      .IsRequired()
                      .HasMaxLength(200);

                entity.Property(p => p.Description)
                      .HasMaxLength(1000);

                entity.Property(p => p.Price)
                      .HasPrecision(10, 2);

                entity.Property(p => p.Category)
                      .HasMaxLength(200);
            });
        }
    }
}
