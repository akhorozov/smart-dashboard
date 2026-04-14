using NRedisStack;
using NRedisStack.DataTypes;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using NRedisStack.Search.Literals.Enums;
using StackExchange.Redis;

namespace RedisSmartDemo.Api.Services;

public class SearchIndexInitializer(IConnectionMultiplexer redis, ILogger<SearchIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();
        await InitProductIndexAsync(db);
        await InitTimeSeriesKeysAsync(db);
        await InitVectorIndexAsync(db);
    }

    private async Task InitProductIndexAsync(IDatabase db)
    {
        var ft = db.FT();
        try
        {
            await ft.CreateAsync(
                "idx:products",
                new FTCreateParams()
                    .On(IndexDataType.JSON)
                    .Prefix("product:"),
                new Schema()
                    .AddTextField(new FieldName("$.Name", "Name"))
                    .AddTextField(new FieldName("$.Description", "Description"))
                    .AddTagField(new FieldName("$.Category", "Category"))
                    .AddNumericField(new FieldName("$.Price", "Price"), sortable: true)
                    .AddTagField(new FieldName("$.Tags[*]", "Tags")));
            logger.LogInformation("Created FT index idx:products");
        }
        catch (RedisServerException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("FT index idx:products already exists");
        }
    }

    private async Task InitTimeSeriesKeysAsync(IDatabase db)
    {
        var ts = db.TS();
        var metrics = new[] { "cpu", "temperature", "latency", "events" };
        foreach (var metric in metrics)
        {
            try
            {
                await ts.CreateAsync(
                    $"ts:{metric}",
                    retentionTime: 86400000L,
                    labels: new[] { new TimeSeriesLabel("metric", metric) });
                logger.LogInformation("Created TS key ts:{Metric}", metric);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug("TS key ts:{Metric} already exists", metric);
            }
        }
    }

    private async Task InitVectorIndexAsync(IDatabase db)
    {
        var ft = db.FT();
        try
        {
            await ft.CreateAsync(
                "idx:products-vec",
                new FTCreateParams()
                    .On(IndexDataType.HASH)
                    .Prefix("vec:product:"),
                new Schema()
                    .AddVectorField("embedding",
                        Schema.VectorField.VectorAlgo.HNSW,
                        new Dictionary<string, object>
                        {
                            ["TYPE"] = "FLOAT32",
                            ["DIM"] = "64",
                            ["DISTANCE_METRIC"] = "COSINE"
                        }));
            logger.LogInformation("Created vector index idx:products-vec");
        }
        catch (RedisServerException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Vector index idx:products-vec already exists");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
