using H3lRa1s3r.Api.CatalogService.Models;
using H3lRa1s3r.Api.CatalogService.Infra;

namespace H3lRa1s3r.Api.CatalogService
{
    public static class Seed
    {
        public static void AddDemoProducts(CatalogDbContext db)
        {
            if (db.Products.Any())
                return;

            var rnd = new Random(42);

            for (int i = 1; i <= 200; i++)
            {
                db.Products.Add(new Product
                {
                    Name = $"Product #{i}",
                    Description = "Demo product seeded into DB",
                    Price = Math.Round((decimal)rnd.NextDouble() * 40 + 10, 2),
                    Category = i % 2 == 0 ? "Men" : "Women",
                    Stock = rnd.Next(10, 200)
                });
            }

            db.SaveChanges();
        }
    }
}
