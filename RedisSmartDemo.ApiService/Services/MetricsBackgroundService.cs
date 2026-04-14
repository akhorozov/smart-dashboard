using NRedisStack;
using NRedisStack.DataTypes;
using NRedisStack.RedisStackCommands;
using StackExchange.Redis;

namespace RedisSmartDemo.Api.Services;

public class MetricsBackgroundService(IConnectionMultiplexer redis, ILogger<MetricsBackgroundService> logger) : BackgroundService
{
    private readonly Random _rng = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(4000, stoppingToken); // wait for Redis init

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var db = redis.GetDatabase();
                var ts = db.TS();
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                await ts.AddAsync("ts:cpu",         new TimeStamp(now), 20 + _rng.NextDouble() * 60);
                await ts.AddAsync("ts:temperature", new TimeStamp(now), 60 + _rng.NextDouble() * 30);
                await ts.AddAsync("ts:latency",     new TimeStamp(now), 5  + _rng.NextDouble() * 45);
                await ts.AddAsync("ts:events",      new TimeStamp(now), (double)_rng.Next(50, 200));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to record metrics");
            }

            await Task.Delay(5000, stoppingToken);
        }
    }
}
