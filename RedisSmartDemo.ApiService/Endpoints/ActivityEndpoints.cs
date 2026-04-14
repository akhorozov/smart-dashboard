using RedisSmartDemo.Api.Models;
using StackExchange.Redis;
using System.Text.Json;

namespace RedisSmartDemo.Api.Endpoints;

public static class ActivityEndpoints
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public static void MapActivityEndpoints(this WebApplication app)
    {
        app.MapPost("/activity/publish", async (ActivityEvent evt, IConnectionMultiplexer redis) =>
        {
            var json = JsonSerializer.Serialize(evt);
            var sub = redis.GetSubscriber();
            await sub.PublishAsync(RedisChannel.Literal("activity"), json);

            var db = redis.GetDatabase();
            await db.ListLeftPushAsync("activity:feed", json);
            await db.ListTrimAsync("activity:feed", 0, 99);

            return Results.Ok(new { published = true });
        });

        app.MapGet("/activity/recent", async (IConnectionMultiplexer redis, int count = 20) =>
        {
            var db = redis.GetDatabase();
            var items = await db.ListRangeAsync("activity:feed", 0, count - 1);
            var events = items
                .Select(item => JsonSerializer.Deserialize<ActivityEvent>(item.ToString(), _json))
                .Where(e => e != null)
                .ToList();
            return Results.Ok(events);
        });

        // SSE endpoint for browser-native EventSource clients
        app.MapGet("/activity/stream", async (HttpContext context, IConnectionMultiplexer redis) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            var sub = redis.GetSubscriber();
            var channel = RedisChannel.Literal("activity");
            var queue = new System.Collections.Concurrent.ConcurrentQueue<string>();

            await sub.SubscribeAsync(channel, (_, msg) => queue.Enqueue(msg.ToString()!));

            try
            {
                while (!context.RequestAborted.IsCancellationRequested)
                {
                    while (queue.TryDequeue(out var message))
                    {
                        await context.Response.WriteAsync($"data: {message}\n\n");
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                    }
                    await Task.Delay(100, context.RequestAborted);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                await sub.UnsubscribeAsync(channel);
            }
        });
    }
}
