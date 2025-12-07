namespace Design_Service.DesignService
{
        public class Design
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("n");
            public string UserId { get; set; } = default!;
            public string Name { get; set; } = default!;
            public string JsonPayload { get; set; } = "{}";
            public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        }
}
