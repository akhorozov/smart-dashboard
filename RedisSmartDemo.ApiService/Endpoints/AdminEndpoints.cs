using NRedisStack;
using NRedisStack.RedisStackCommands;
using StackExchange.Redis;

namespace RedisSmartDemo.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        // Bloom filter — email uniqueness
        app.MapPost("/bloom/check-email", async (EmailRequest req, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var exists = await db.BF().ExistsAsync("bf:emails", req.Email);
            return Results.Ok(new { email = req.Email, exists });
        });

        app.MapPost("/bloom/register-email", async (EmailRequest req, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var wasNew = await db.BF().AddAsync("bf:emails", req.Email);
            return Results.Ok(new { email = req.Email, wasNew, registered = true });
        });

        // Bloom filter — product views per user
        app.MapPost("/bloom/check-product-view", async (ProductViewRequest req, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var seen = await db.BF().ExistsAsync($"bf:seen:{req.UserId}", req.ProductId);
            return Results.Ok(new { req.UserId, req.ProductId, seen });
        });

        app.MapPost("/bloom/record-product-view", async (ProductViewRequest req, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            await db.BF().AddAsync($"bf:seen:{req.UserId}", req.ProductId);
            return Results.Ok(new { req.UserId, req.ProductId, recorded = true });
        });

        // Sliding-window rate limiter (10 requests/minute per user)
        app.MapGet("/ratelimit/check/{userId}", async (string userId, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var key = $"ratelimit:{userId}:{DateTime.UtcNow:yyyyMMddHHmm}";
            const int limit = 10;

            var count = await db.StringIncrementAsync(key);
            if (count == 1) await db.KeyExpireAsync(key, TimeSpan.FromMinutes(1));

            return Results.Ok(new
            {
                userId,
                limit,
                used = (int)count,
                remaining = Math.Max(0, limit - (int)count),
                isLimited = count > limit
            });
        });
    }
}

public record EmailRequest(string Email);
public record ProductViewRequest(string UserId, string ProductId);
