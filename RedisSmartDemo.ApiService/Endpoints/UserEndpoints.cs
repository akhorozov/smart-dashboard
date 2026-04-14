using NRedisStack;
using NRedisStack.RedisStackCommands;
using RedisSmartDemo.Api.Models;
using StackExchange.Redis;
using System.Text.Json;

namespace RedisSmartDemo.Api.Endpoints;

public static class UserEndpoints
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public static void MapUserEndpoints(this WebApplication app)
    {
        app.MapPost("/users", async (User user, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            await db.JSON().SetAsync($"user:{user.Id}", "$", user);
            await db.SetAddAsync("users", user.Id);
            await PublishActivity(redis, "user", "created", user.Id, $"User '{user.Name}' joined");
            return Results.Created($"/users/{user.Id}", user);
        });

        app.MapGet("/users", async (IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var ids = await db.SetMembersAsync("users");
            var users = new List<User>();
            var jsonDb = db.JSON();
            foreach (var id in ids)
            {
                var user = await jsonDb.GetAsync<User>($"user:{id}");
                if (user != null) users.Add(user);
            }
            return Results.Ok(users);
        });

        app.MapGet("/users/{id}", async (string id, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();

            var cached = await db.StringGetAsync($"cache:user:{id}");
            if (cached.HasValue)
            {
                await db.StringIncrementAsync("stats:cache:hits");
                var cachedUser = JsonSerializer.Deserialize<User>(cached.ToString(), _json);
                return cachedUser is null ? Results.NotFound() : Results.Ok(cachedUser);
            }

            await db.StringIncrementAsync("stats:cache:misses");
            var user = await db.JSON().GetAsync<User>($"user:{id}");
            if (user is null) return Results.NotFound();

            await db.StringSetAsync($"cache:user:{id}", JsonSerializer.Serialize(user), TimeSpan.FromSeconds(60));
            return Results.Ok(user);
        });

        app.MapPut("/users/{id}", async (string id, User updated, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            if (!await db.KeyExistsAsync($"user:{id}")) return Results.NotFound();

            updated.Id = id;
            await db.JSON().SetAsync($"user:{id}", "$", updated);
            await db.KeyDeleteAsync($"cache:user:{id}");
            await PublishActivity(redis, "user", "updated", id, $"User '{updated.Name}' was updated");
            return Results.Ok(updated);
        });

        app.MapDelete("/users/{id}", async (string id, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var removed = await db.KeyDeleteAsync($"user:{id}");
            if (!removed) return Results.NotFound();

            await db.SetRemoveAsync("users", id);
            await db.KeyDeleteAsync($"cache:user:{id}");
            return Results.NoContent();
        });
    }

    internal static async Task PublishActivity(IConnectionMultiplexer redis, string type, string action, string entityId, string message)
    {
        try
        {
            var evt = new ActivityEvent { Type = type, Action = action, EntityId = entityId, Message = message };
            var json = JsonSerializer.Serialize(evt);
            var sub = redis.GetSubscriber();
            await sub.PublishAsync(RedisChannel.Literal("activity"), json);
            var db = redis.GetDatabase();
            await db.ListLeftPushAsync("activity:feed", json);
            await db.ListTrimAsync("activity:feed", 0, 99);
        }
        catch { /* Non-critical */ }
    }
}
