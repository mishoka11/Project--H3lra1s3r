namespace H3lRa1s3r.Api.OrderService
{
    public class Models
    {
        public class OrderItem
        {
            public string ProductId { get; set; } = default!;
            public int Quantity { get; set; }
            public decimal UnitPrice { get; set; }
        }

        public class Order
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("n");
            public string UserId { get; set; } = default!;
            public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
            public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
            public string Status { get; set; } = "Created";
        }
    }
}
