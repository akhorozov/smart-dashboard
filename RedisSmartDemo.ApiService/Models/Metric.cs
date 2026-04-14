namespace RedisSmartDemo.Api.Models;

public class MetricRecord
{
    public string MetricName { get; set; } = "";
    public double Value { get; set; }
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
