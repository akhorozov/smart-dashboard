using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.DataTypes;
using RedisSmartDemo.Api.Models;
using StackExchange.Redis;

namespace RedisSmartDemo.Api.Endpoints;

public static class MetricEndpoints
{
    public static void MapMetricEndpoints(this WebApplication app)
    {
        app.MapPost("/metrics/record", async (MetricRecord record, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var ts = db.TS();
            var timestamp = record.Timestamp > 0 ? record.Timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await ts.AddAsync($"ts:{record.MetricName}", new TimeStamp(timestamp), record.Value);
            return Results.Ok(new { recorded = true, timestamp });
        });

        app.MapGet("/metrics/{name}", async (string name, long? from, long? to, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var ts = db.TS();

            var fromTs = from.HasValue ? new TimeStamp(from.Value) : new TimeStamp("-");
            var toTs   = to.HasValue   ? new TimeStamp(to.Value)   : new TimeStamp("+");

            var range = await ts.RangeAsync($"ts:{name}", fromTs, toTs);
            var dataPoints = range.Select(p => new { timestamp = p.Time.Value, value = p.Val }).ToList();
            return Results.Ok(dataPoints);
        });

        app.MapGet("/metrics", async (IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var ts = db.TS();
            var metrics = new[] { "cpu", "temperature", "latency", "events" };
            var result = new Dictionary<string, object>();
            var fiveMinutesAgo = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 5 * 60 * 1000;

            foreach (var metric in metrics)
            {
                try
                {
                    var range = await ts.RangeAsync($"ts:{metric}", new TimeStamp(fiveMinutesAgo), new TimeStamp("+"));
                    result[metric] = range.Select(p => new { timestamp = p.Time.Value, value = p.Val }).ToList();
                }
                catch
                {
                    result[metric] = new List<object>();
                }
            }

            return Results.Ok(result);
        });
    }
}
