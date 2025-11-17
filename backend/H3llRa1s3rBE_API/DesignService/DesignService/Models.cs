namespace H3lRa1s3r.Api.DesignService
{
    public class Models
    {
        public class Design
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("n");
            public string UserId { get; set; } = default!;
            public string Name { get; set; } = default!;
            public string JsonPayload { get; set; } = "{}";
            public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        }

        // Keeping the old dictionary for compatibility (not used by EF)
        public static class DesignsDb
        {
            public static readonly Dictionary<string, Design> Designs = new();
        }
    }
}
