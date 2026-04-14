namespace RedisSmartDemo.Web.Models;

public record User(string Id, string Name, string Email, UserPreferences? Preferences = null);
public record UserPreferences(string Theme = "light", string Language = "en");

public record Product(string Id, string Name, string Description, string Category, decimal Price, string[]? Tags = null);

public record MetricDataPoint(long Timestamp, double Value)
{
    public DateTime DateTime => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp).LocalDateTime;
}

public record ActivityEvent(string Type, string Action, string EntityId, string Message, DateTime Timestamp);

public record Recommendation(string ProductId, double Score);

public record CacheStats(long Hits, long Misses, double HitRate);

public record RateLimitResult(string UserId, int Limit, int Used, int Remaining, bool IsLimited);

public record BloomCheckResult(string? Email, bool Exists);

public record ProductViewResult(string UserId, string ProductId, bool Seen);
