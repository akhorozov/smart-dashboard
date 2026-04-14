namespace RedisSmartDemo.Api.Models;

public class ActivityEvent
{
    public string Type { get; set; } = "system";   // user | product | system
    public string Action { get; set; } = "";        // created | updated | deleted | viewed
    public string EntityId { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
