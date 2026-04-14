using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using RedisSmartDemo.Api.Models;
using StackExchange.Redis;
using System.Text.Json;

namespace RedisSmartDemo.Api.Endpoints;

public static class ProductEndpoints
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public static void MapProductEndpoints(this WebApplication app)
    {
        app.MapPost("/products", async (Product product, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            await db.JSON().SetAsync($"product:{product.Id}", "$", product);
            await db.SetAddAsync("products", product.Id);
            await UserEndpoints.PublishActivity(redis, "product", "created", product.Id, $"Product '{product.Name}' added");
            return Results.Created($"/products/{product.Id}", product);
        });

        app.MapGet("/products", async (string? q, string? category, decimal? minPrice, decimal? maxPrice, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var ft = db.FT();

            var queryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(q))
                queryParts.Add(q);
            if (!string.IsNullOrWhiteSpace(category))
                queryParts.Add($"@Category:{{{category}}}");
            if (minPrice.HasValue || maxPrice.HasValue)
            {
                var min = minPrice.HasValue ? minPrice.Value.ToString("F2") : "-inf";
                var max = maxPrice.HasValue ? maxPrice.Value.ToString("F2") : "+inf";
                queryParts.Add($"@Price:[{min} {max}]");
            }

            var queryStr = queryParts.Count > 0 ? string.Join(" ", queryParts) : "*";

            var results = await ft.SearchAsync("idx:products", new Query(queryStr).Limit(0, 50));
            var products = new List<Product>();

            foreach (var doc in results.Documents)
            {
                var product = await db.JSON().GetAsync<Product>(doc.Id);
                if (product != null) products.Add(product);
            }

            return Results.Ok(products);
        });

        app.MapGet("/products/{id}", async (string id, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();

            var cached = await db.StringGetAsync($"cache:product:{id}");
            if (cached.HasValue)
            {
                await db.StringIncrementAsync("stats:cache:hits");
                var cachedProduct = JsonSerializer.Deserialize<Product>(cached.ToString(), _json);
                return cachedProduct is null ? Results.NotFound() : Results.Ok(cachedProduct);
            }

            await db.StringIncrementAsync("stats:cache:misses");
            var product = await db.JSON().GetAsync<Product>($"product:{id}");
            if (product is null) return Results.NotFound();

            await db.StringSetAsync($"cache:product:{id}", JsonSerializer.Serialize(product), TimeSpan.FromSeconds(60));
            await UserEndpoints.PublishActivity(redis, "product", "viewed", id, $"Product '{product.Name}' was viewed");
            return Results.Ok(product);
        });

        app.MapPut("/products/{id}", async (string id, Product updated, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            if (!await db.KeyExistsAsync($"product:{id}")) return Results.NotFound();

            updated.Id = id;
            await db.JSON().SetAsync($"product:{id}", "$", updated);
            await db.KeyDeleteAsync($"cache:product:{id}");
            return Results.Ok(updated);
        });

        app.MapDelete("/products/{id}", async (string id, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var removed = await db.KeyDeleteAsync($"product:{id}");
            if (!removed) return Results.NotFound();

            await db.SetRemoveAsync("products", id);
            await db.KeyDeleteAsync($"cache:product:{id}");
            return Results.NoContent();
        });
    }
}
