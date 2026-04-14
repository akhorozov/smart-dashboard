using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;

namespace RedisSmartDemo.Api.Endpoints;

public static class RecommendationEndpoints
{
    private const int VectorDim = 64;

    public static void MapRecommendationEndpoints(this WebApplication app)
    {
        // Embed a product: generate deterministic mock vector and store as HASH
        app.MapPost("/recommendations/embed/{productId}", async (string productId, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var vectorBytes = GenerateEmbeddingBytes(productId);
            await db.HashSetAsync($"vec:product:{productId}", [
                new HashEntry("embedding", vectorBytes),
                new HashEntry("productId", productId)
            ]);
            return Results.Ok(new { productId, vectorDim = VectorDim });
        });

        // KNN query: find products similar to userId's preference vector
        app.MapGet("/recommendations/{userId}", async (string userId, IConnectionMultiplexer redis, int count = 5) =>
        {
            var db = redis.GetDatabase();
            var queryVectorBytes = GenerateEmbeddingBytes(userId);

            var query = new Query($"*=>[KNN {count} @embedding $vec AS score]")
                .AddParam("vec", queryVectorBytes)
                .ReturnFields("productId", "score")
                .SetSortBy("score")
                .Dialect(2);

            var results = await db.FT().SearchAsync("idx:products-vec", query);

            var recommendations = results.Documents
                .Select(doc => new
                {
                    productId = doc["productId"].ToString(),
                    score = double.TryParse(doc["score"].ToString(), out var s) ? Math.Round(1.0 - s, 4) : 0d
                })
                .ToList();

            return Results.Ok(recommendations);
        });
    }

    private static byte[] GenerateEmbeddingBytes(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var vector = new float[VectorDim];
        double magnitude = 0;

        for (int i = 0; i < VectorDim; i++)
        {
            vector[i] = (hash[i % hash.Length] / 128f) - 1f;
            magnitude += vector[i] * vector[i];
        }

        magnitude = Math.Sqrt(magnitude);
        for (int i = 0; i < VectorDim; i++)
            vector[i] /= (float)magnitude;

        var bytes = new byte[VectorDim * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
