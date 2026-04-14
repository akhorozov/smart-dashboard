using StackExchange.Redis;

namespace RedisSmartDemo.Api.Endpoints;

public static class CacheEndpoints
{
    public static void MapCacheEndpoints(this WebApplication app)
    {
        app.MapGet("/cache/stats", async (IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var hits   = (long?)await db.StringGetAsync("stats:cache:hits")   ?? 0;
            var misses = (long?)await db.StringGetAsync("stats:cache:misses") ?? 0;
            var total  = hits + misses;
            return Results.Ok(new
            {
                hits,
                misses,
                hitRate = total == 0 ? 0.0 : Math.Round((double)hits / total, 4)
            });
        });

        app.MapDelete("/cache/{key}", async (string key, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var removed = await db.KeyDeleteAsync($"cache:{key}");
            return Results.Ok(new { key = $"cache:{key}", removed });
        });

        app.MapDelete("/cache", async (IConnectionMultiplexer redis) =>
        {
            var server = redis.GetServer(redis.GetEndPoints().First());
            var keys = server.Keys(pattern: "cache:*").ToArray();
            var db = redis.GetDatabase();
            if (keys.Length > 0) await db.KeyDeleteAsync(keys);
            await db.KeyDeleteAsync("stats:cache:hits");
            await db.KeyDeleteAsync("stats:cache:misses");
            return Results.Ok(new { cleared = keys.Length });
        });
    }
}
