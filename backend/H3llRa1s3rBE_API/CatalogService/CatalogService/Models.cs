namespace H3lRa1s3r.Api.CatalogService
{
    public class Models
    {
        public record Product(string Id, string Name, decimal Price, string[] Tags);

        public static class Db
        {
            public static readonly Dictionary<string, Product> Products = new();
        }

    }
}
