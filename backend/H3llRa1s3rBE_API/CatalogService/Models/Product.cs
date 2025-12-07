namespace H3lRa1s3r.Api.CatalogService.Models
{
    public class Product
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("n");
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Category { get; set; } = "General";
        public int Stock { get; set; } = 0;
    }
}
