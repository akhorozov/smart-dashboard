using NRedisStack;
using NRedisStack.RedisStackCommands;
using RedisSmartDemo.Api.Models;
using Scalar.AspNetCore;
using StackExchange.Redis;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

builder.AddRedisClient("redis");

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

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

app.MapPost("/products", async (Product product, IConnectionMultiplexer redis) =>
{
    if (string.IsNullOrWhiteSpace(product.Id))
        product.Id = Guid.NewGuid().ToString();

    var db = redis.GetDatabase();
    var jsonDb = db.JSON();

    await jsonDb.SetAsync($"product:{product.Id}", "$", product);
    await db.SetAddAsync("products", product.Id);

    return Results.Created($"/products/{product.Id}", product);
});

app.MapGet("/products/search", async (string? q, string? category, decimal? minPrice, decimal? maxPrice, string? sortBy, IConnectionMultiplexer redis) =>
{
    if (minPrice.HasValue && maxPrice.HasValue && minPrice > maxPrice)
        return Results.BadRequest("minPrice cannot be greater than maxPrice.");

    var db = redis.GetDatabase();
    var jsonDb = db.JSON();

    var queryParts = new List<string>();

    if (!string.IsNullOrWhiteSpace(q))
        queryParts.Add(q);

    if (!string.IsNullOrWhiteSpace(category))
        queryParts.Add($"@category:{{{EscapeTagValue(category)}}}");

    if (minPrice.HasValue || maxPrice.HasValue)
    {
        var min = minPrice?.ToString(CultureInfo.InvariantCulture) ?? "-inf";
        var max = maxPrice?.ToString(CultureInfo.InvariantCulture) ?? "+inf";
        queryParts.Add($"@price:[{min} {max}]");
    }

    var query = queryParts.Count == 0 ? "*" : string.Join(" ", queryParts);
    var args = new List<object> { "idx:products", query, "NOCONTENT" };

    var (sortField, sortOrder) = ParseSort(sortBy);
    if (sortField != null)
    {
        args.Add("SORTBY");
        args.Add(sortField);
        args.Add(sortOrder!);
    }

    RedisResult searchResult;
    try
    {
        searchResult = await db.ExecuteAsync("FT.SEARCH", args.ToArray());
    }
    catch (RedisServerException ex) when (ex.Message.Contains("Unknown Index name", StringComparison.OrdinalIgnoreCase))
    {
        await EnsureProductsIndexAsync(db);
        searchResult = await db.ExecuteAsync("FT.SEARCH", args.ToArray());
    }

    RedisResult[]? results = (RedisResult[]?)searchResult;
    if (results is null || results.Length <= 1)
        return Results.Ok(Array.Empty<Product>());

    var products = new List<Product>();
    for (var i = 1; i < results.Length; i++)
    {
        var key = (string?)results[i];
        if (string.IsNullOrWhiteSpace(key))
            continue;

        var product = await jsonDb.GetAsync<Product>(key);
        if (product is not null)
            products.Add(product);
    }

    return Results.Ok(products);
});

app.MapGet("/products/{id}", async (string id, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    var jsonDb = db.JSON();

    var product = await jsonDb.GetAsync<Product>($"product:{id}");
    if (product is null)
        return Results.NotFound();

    return Results.Ok(product);
});

app.MapPut("/products/{id}", async (string id, Product updated, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();

    var exists = await db.KeyExistsAsync($"product:{id}");
    if (!exists)
        return Results.NotFound();

    updated.Id = id;

    var jsonDb = db.JSON();
    await jsonDb.SetAsync($"product:{id}", "$", updated);

    return Results.Ok(updated);
});

app.MapDelete("/products/{id}", async (string id, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();

    var removed = await db.KeyDeleteAsync($"product:{id}");
    if (!removed)
        return Results.NotFound();

    await db.SetRemoveAsync("products", id);

    return Results.NoContent();
});

app.MapDefaultEndpoints();

app.Run();

static async Task EnsureProductsIndexAsync(IDatabase db)
{
    try
    {
        await db.ExecuteAsync(
            "FT.CREATE",
            "idx:products",
            "ON", "JSON",
            "PREFIX", "1", "product:",
            "SCHEMA",
            "$.name", "AS", "name", "TEXT",
            "$.description", "AS", "description", "TEXT",
            "$.category", "AS", "category", "TAG", "SORTABLE",
            "$.price", "AS", "price", "NUMERIC", "SORTABLE");
    }
    catch (RedisServerException ex) when (ex.Message.Contains("Index already exists", StringComparison.OrdinalIgnoreCase))
    {
        // no-op
    }
}

static string EscapeTagValue(string value) =>
    value.Replace(@"\", @"\\").Replace("-", @"\-").Replace("|", @"\|").Replace("{", @"\{").Replace("}", @"\}");

static (string? field, string? order) ParseSort(string? sortBy)
{
    if (string.IsNullOrWhiteSpace(sortBy))
        return (null, null);

    return sortBy.Trim().ToLowerInvariant() switch
    {
        "name" or "name_asc" => ("name", "ASC"),
        "-name" or "name_desc" => ("name", "DESC"),
        "category" or "category_asc" => ("category", "ASC"),
        "-category" or "category_desc" => ("category", "DESC"),
        "price" or "price_asc" => ("price", "ASC"),
        "-price" or "price_desc" => ("price", "DESC"),
        _ => (null, null)
    };
}
