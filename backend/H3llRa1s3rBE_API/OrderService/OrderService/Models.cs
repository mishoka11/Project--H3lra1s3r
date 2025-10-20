namespace H3lRa1s3r.Api.OrderService
{
    public class Models
    {
        public record OrderItem(string ProductId, int Quantity, decimal UnitPrice);
        public record Order(string Id, string UserId, DateTimeOffset CreatedAt, OrderItem[] Items, string Status);
        public static class OrdersDb { public static readonly Dictionary<string, Order> Orders = new(); }

    }
}
