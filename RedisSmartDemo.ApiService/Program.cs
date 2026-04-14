using NRedisStack;
using NRedisStack.RedisStackCommands;
using RedisSmartDemo.Api.Models;
using Scalar.AspNetCore;
using StackExchange.Redis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

builder.AddRedisClient("redis");

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();
const int VectorDimensions = 384;
const int VectorByteLength = VectorDimensions * sizeof(float);
const int MinimumSearchResultLength = 3;
const int RecommendationTopK = 5;
const string ProductKeyPrefix = "product:";
const string ProductVectorIndex = "products:vec";
const string EmbeddingFieldName = "Embedding";

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapPost("/users", async (User user, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    var jsonDb = db.JSON();

    await jsonDb.SetAsync($"user:{user.Id}", "$", user);
    await db.SetAddAsync("users", user.Id);

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
        if (user != null)
            users.Add(user);
    }

    return users;
});


app.MapGet("/users/{id}", async (string id, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    var jsonDb = db.JSON();

    var user = await jsonDb.GetAsync<User>($"user:{id}");
    if (user is null)
        return Results.NotFound();

    return Results.Ok(user);
});


app.MapPut("/users/{id}", async (string id, User updated, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();

    // Ensure the user exists
    var exists = await db.KeyExistsAsync($"user:{id}");
    if (!exists)
        return Results.NotFound();

    updated.Id = id; // keep original ID

    var jsonDb = db.JSON();
    await jsonDb.SetAsync($"user:{id}", "$", updated);

    return Results.Ok(updated);
});

app.MapDelete("/users/{id}", async (string id, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();

    var removed = await db.KeyDeleteAsync($"user:{id}");
    if (!removed)
        return Results.NotFound();

    await db.SetRemoveAsync("users", id);

    return Results.NoContent();
});

app.MapPost("/recommendations/embed", async (EmbedRecommendationRequest request, IConnectionMultiplexer redis) =>
{
    if (string.IsNullOrWhiteSpace(request.ProductId) || string.IsNullOrWhiteSpace(request.ProductName))
        return Results.BadRequest("ProductId and ProductName are required.");

    var embedding = BuildDeterministicEmbedding(request.ProductName);
    var db = redis.GetDatabase();

    await db.HashSetAsync($"{ProductKeyPrefix}{request.ProductId}", [
        new HashEntry("Id", request.ProductId),
        new HashEntry("Name", request.ProductName),
        new HashEntry(EmbeddingFieldName, embedding)
    ]);

    return Results.Ok(new
    {
        request.ProductId,
        request.ProductName,
        Dimensions = VectorDimensions
    });
});

app.MapGet("/recommendations/{userId}", async (string userId, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();

    var purchasesTask = LoadHistoryProductIdsAsync(db, $"user:{userId}:purchases");
    var viewsTask = LoadHistoryProductIdsAsync(db, $"user:{userId}:views");
    await Task.WhenAll(purchasesTask, viewsTask);

    var purchases = await purchasesTask;
    var views = await viewsTask;

    var historyProductIds = purchases
        .Concat(views)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    if (historyProductIds.Length == 0)
        return Results.Ok(Array.Empty<RecommendationResult>());

    var vectors = new List<float[]>(historyProductIds.Length);
    foreach (var productId in historyProductIds)
    {
        var productKey = $"{ProductKeyPrefix}{productId}";
        if (!await db.KeyExistsAsync(productKey))
        {
            if (await db.KeyExistsAsync(productId))
                productKey = productId;
            else
                continue;
        }

        var embeddingValue = await db.HashGetAsync(productKey, EmbeddingFieldName);
        if (!embeddingValue.HasValue)
            continue;

        var vector = ParseEmbeddingBytes(embeddingValue);
        if (vector is not null)
            vectors.Add(vector);
    }

    if (vectors.Count == 0)
        return Results.Ok(Array.Empty<RecommendationResult>());

    var preferenceVector = BuildPreferenceVector(vectors);

    var searchResponse = await db.ExecuteAsync(
        "FT.SEARCH",
        ProductVectorIndex,
        $"*=>[KNN {RecommendationTopK} @{EmbeddingFieldName} $vector AS score]",
        "PARAMS",
        "2",
        "vector",
        preferenceVector,
        "SORTBY",
        "score",
        "ASC",
        "RETURN",
        "3",
        "Id",
        "Name",
        "score",
        "DIALECT",
        "2");

    var results = ParseRecommendationSearchResult(searchResponse)
        .Take(RecommendationTopK)
        .ToArray();

    return Results.Ok(results);
});

app.MapDefaultEndpoints();

app.Run();

static byte[] BuildDeterministicEmbedding(string productName)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(productName));
    var vector = new float[VectorDimensions];

    for (var i = 0; i < VectorDimensions; i++)
    {
        uint value = 0;
        for (var j = 0; j < sizeof(uint); j++)
        {
            value = (value << 8) | hash[((i * sizeof(uint)) + j) % hash.Length];
        }

        vector[i] = value / (float)uint.MaxValue;
    }

    return ToBytes(vector);
}

static float[] BuildPreferenceVector(IEnumerable<float[]> vectors)
{
    var sum = new float[VectorDimensions];
    var count = 0;

    foreach (var vector in vectors)
    {
        if (vector.Length != VectorDimensions)
            continue;

        for (var i = 0; i < VectorDimensions; i++)
            sum[i] += vector[i];

        count++;
    }

    if (count == 0)
        return sum;

    for (var i = 0; i < VectorDimensions; i++)
        sum[i] /= count;

    return sum;
}

static float[]? ParseEmbeddingBytes(RedisValue embedding)
{
    var bytes = (byte[]?)embedding;
    if (bytes is null || bytes.Length != VectorByteLength)
        return null;

    var vector = new float[VectorDimensions];
    Buffer.BlockCopy(bytes, 0, vector, 0, VectorByteLength);
    return vector;
}

static byte[] ToBytes(float[] vector)
{
    var bytes = new byte[vector.Length * sizeof(float)];
    Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
    return bytes;
}

static async Task<IReadOnlyList<string>> LoadHistoryProductIdsAsync(IDatabase db, string historyKey)
{
    var keyType = await db.KeyTypeAsync(historyKey);
    return keyType switch
    {
        RedisType.Set => (await db.SetMembersAsync(historyKey))
            .Where(static v => v.HasValue)
            .Select(static v => v.ToString())
            .ToArray(),
        RedisType.List => (await db.ListRangeAsync(historyKey))
            .Where(static v => v.HasValue)
            .Select(static v => v.ToString())
            .ToArray(),
        RedisType.SortedSet => (await db.SortedSetRangeByRankAsync(historyKey))
            .Where(static v => v.HasValue)
            .Select(static v => v.ToString())
            .ToArray(),
        _ => []
    };
}

static IReadOnlyList<RecommendationResult> ParseRecommendationSearchResult(RedisResult searchResponse)
{
    if (searchResponse.IsNull)
        return [];

    var resultArray = (RedisResult[])searchResponse!;
    if (resultArray.Length < MinimumSearchResultLength)
        return [];

    var recommendations = new List<RecommendationResult>();
    for (var i = 1; i + 1 < resultArray.Length; i += 2)
    {
        var documentId = resultArray[i].ToString();
        var fieldsArray = (RedisResult[])resultArray[i + 1]!;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var j = 0; j + 1 < fieldsArray.Length; j += 2)
            fields[fieldsArray[j].ToString()] = fieldsArray[j + 1].ToString();

        var id = GetProductId(documentId, fields);

        var name = fields.TryGetValue("Name", out var productName) ? productName : string.Empty;
        var scoreText = fields.TryGetValue("score", out var scoreValue) ? scoreValue : "0";
        _ = float.TryParse(scoreText, NumberStyles.Float, CultureInfo.InvariantCulture, out var score);

        recommendations.Add(new RecommendationResult(id, name, score));
    }

    return recommendations;
}

static string GetProductId(string documentId, IDictionary<string, string> fields)
{
    if (fields.TryGetValue("Id", out var productId) && !string.IsNullOrWhiteSpace(productId))
        return productId;

    if (documentId.StartsWith(ProductKeyPrefix, StringComparison.Ordinal))
        return documentId[ProductKeyPrefix.Length..];

    return documentId;
}

record EmbedRecommendationRequest(string ProductId, string ProductName);
record RecommendationResult(string ProductId, string ProductName, float Score);
