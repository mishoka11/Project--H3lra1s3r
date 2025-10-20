using static H3lRa1s3r.Api.CatalogService.Models;

namespace H3lRa1s3r.Api.CatalogService
{
    public static class Seed
    {
        public static void AddDemoProducts()
        {
            var rnd = new Random(42);
            for (int i = 1; i <= 200; i++)
            {
                var p = new Product(
                    i.ToString(),
                    $"Tee #{i}",
                    Math.Round((decimal)rnd.NextDouble() * 40m + 10m, 2),
                    new[] { "apparel", i % 2 == 0 ? "men" : "women" }
                );
                Db.Products[p.Id] = p;
            }
        }
    }

}
