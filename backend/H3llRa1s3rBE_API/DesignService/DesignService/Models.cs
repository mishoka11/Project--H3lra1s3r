namespace H3lRa1s3r.Api.DesignService
{
    public class Models
    {
        public record Design(string Id, string UserId, string Name, string JsonPayload, DateTimeOffset CreatedAt);
        public static class DesignsDb { public static readonly Dictionary<string, Design> Designs = new(); }

    }
}
