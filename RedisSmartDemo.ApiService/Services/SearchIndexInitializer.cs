using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace RedisSmartDemo.Api.Services;

public sealed class SearchIndexInitializer(IConnectionMultiplexer redis) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();

        try
        {
            await db.ExecuteAsync(
                "FT.CREATE",
                "products",
                "ON",
                "JSON",
                "SCHEMA",
                "$.Name",
                "AS",
                "Name",
                "TEXT",
                "$.Category",
                "AS",
                "Category",
                "TAG",
                "$.Price",
                "AS",
                "Price",
                "NUMERIC",
                "SORTABLE",
                "$.Tags[*]",
                "AS",
                "Tags",
                "TAG");
        }
        catch (RedisServerException ex) when (ex.Message.Contains("Index already exists", StringComparison.OrdinalIgnoreCase))
        {
            // Index already exists; no action needed.
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
