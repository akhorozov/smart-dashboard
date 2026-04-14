using System.Globalization;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace RedisSmartDemo.ApiService.Services;

public sealed class MetricsBackgroundService(
    IConnectionMultiplexer redis,
    ILogger<MetricsBackgroundService> logger) : BackgroundService
{
    private static readonly string[] MetricKeys =
    [
        "metrics:cpu",
        "metrics:temperature",
        "metrics:latency",
        "metrics:events"
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = redis.GetDatabase();

        await EnsureTimeSeriesKeysAsync(db);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AddMetricsPointAsync(db);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to add one or more metrics datapoints.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task EnsureTimeSeriesKeysAsync(IDatabase db)
    {
        foreach (var key in MetricKeys)
        {
            try
            {
                await db.ExecuteAsync("TS.CREATE", key);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug(ex, "Time series key {Key} already exists.", key);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create time series key {Key}.", key);
            }
        }
    }

    private static Task AddMetricsPointAsync(IDatabase db)
    {
        var cpu = Random.Shared.NextDouble() * 100d;
        var temperature = 20d + (Random.Shared.NextDouble() * 60d);
        var latency = 1d + (Random.Shared.NextDouble() * 499d);
        var events = Random.Shared.Next(0, 1001);

        return Task.WhenAll(
            db.ExecuteAsync("TS.ADD", "metrics:cpu", "*", cpu.ToString(CultureInfo.InvariantCulture)),
            db.ExecuteAsync("TS.ADD", "metrics:temperature", "*", temperature.ToString(CultureInfo.InvariantCulture)),
            db.ExecuteAsync("TS.ADD", "metrics:latency", "*", latency.ToString(CultureInfo.InvariantCulture)),
            db.ExecuteAsync("TS.ADD", "metrics:events", "*", events));
    }
}
