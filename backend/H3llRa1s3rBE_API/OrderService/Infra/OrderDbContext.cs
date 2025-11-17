using Microsoft.EntityFrameworkCore;
using static H3lRa1s3r.Api.OrderService.Models;

namespace OrderService.Infra
{
    public class OrderDbContext : DbContext
    {
        public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }

        protected override void OnModelCreating(ModelBuilder b)
        {
            // ---- Order table ----
            b.Entity<Order>(e =>
            {
                e.ToTable("Orders");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).IsRequired().HasMaxLength(64);
                e.Property(x => x.Status).HasMaxLength(64);
                e.Property(x => x.UserId).IsRequired();
                e.Property(x => x.CreatedAt).IsRequired();

                // ---- Owned OrderItems ----
                e.OwnsMany(o => o.Items, nb =>
                {
                    nb.ToTable("OrderItems");
                    nb.WithOwner().HasForeignKey("OrderId");
                    nb.Property<string>("OrderId").IsRequired();
                    nb.HasKey("OrderId", "ProductId");

                    nb.Property(i => i.ProductId).IsRequired();
                    nb.Property(i => i.Quantity).IsRequired();
                    nb.Property(i => i.UnitPrice).HasColumnType("decimal(10,2)");
                });
            });

            base.OnModelCreating(b);
        }
    }
}
