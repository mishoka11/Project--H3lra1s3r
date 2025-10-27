using Microsoft.EntityFrameworkCore;
using static H3lRa1s3r.Api.OrderService.Models;

namespace OrderService.Infra
{
    public class OrderDbContext : DbContext
    {
        public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Order>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Status).HasMaxLength(64);
                e.OwnsMany(x => x.Items, nb =>
                {
                    nb.WithOwner().HasForeignKey("OrderId");
                    nb.Property<string>("OrderId");
                    nb.HasKey("OrderId", "ProductId");
                });
            });
        }
    }
}
