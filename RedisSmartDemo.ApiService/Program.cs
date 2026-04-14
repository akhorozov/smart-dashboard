using NRedisStack;
using NRedisStack.RedisStackCommands;
using RedisSmartDemo.Api.Models;
using Scalar.AspNetCore;
using StackExchange.Redis;

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
    await db.KeyDeleteAsync($"cache:user:{user.Id}");

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


app.MapGet("/users/{id}", async (string id, IConnectionMultiplexer redis, HttpContext httpContext) =>
{
    var db = redis.GetDatabase();
    var jsonDb = db.JSON();
    var cacheKey = $"cache:user:{id}";

    var cachedUser = await jsonDb.GetAsync<User>(cacheKey);
    if (cachedUser is not null)
    {
        httpContext.Response.Headers["X-Cache"] = "HIT";
        return Results.Ok(cachedUser);
    }

    var user = await jsonDb.GetAsync<User>($"user:{id}");
    if (user is null)
        return Results.NotFound();

    await jsonDb.SetAsync(cacheKey, "$", user);
    await db.KeyExpireAsync(cacheKey, TimeSpan.FromMinutes(5));

    httpContext.Response.Headers["X-Cache"] = "MISS";
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
    await db.KeyDeleteAsync($"cache:user:{id}");

    return Results.Ok(updated);
});

app.MapDelete("/users/{id}", async (string id, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();

    var removed = await db.KeyDeleteAsync($"user:{id}");
    if (!removed)
        return Results.NotFound();

    await db.SetRemoveAsync("users", id);
    await db.KeyDeleteAsync($"cache:user:{id}");

    return Results.NoContent();
});

app.MapPost("/products", async (Product product, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    var jsonDb = db.JSON();

    await jsonDb.SetAsync($"product:{product.Id}", "$", product);
    await db.SetAddAsync("products", product.Id);
    await db.KeyDeleteAsync($"cache:product:{product.Id}");

    return Results.Created($"/products/{product.Id}", product);
});

app.MapGet("/products", async (IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();

    var ids = await db.SetMembersAsync("products");
    var products = new List<Product>();

    var jsonDb = db.JSON();
    foreach (var id in ids)
    {
        var product = await jsonDb.GetAsync<Product>($"product:{id}");
        if (product != null)
            products.Add(product);
    }

    return products;
});

app.MapGet("/products/{id}", async (string id, IConnectionMultiplexer redis, HttpContext httpContext) =>
{
    var db = redis.GetDatabase();
    var jsonDb = db.JSON();
    var cacheKey = $"cache:product:{id}";

    var cachedProduct = await jsonDb.GetAsync<Product>(cacheKey);
    if (cachedProduct is not null)
    {
        httpContext.Response.Headers["X-Cache"] = "HIT";
        return Results.Ok(cachedProduct);
    }

    var product = await jsonDb.GetAsync<Product>($"product:{id}");
    if (product is null)
        return Results.NotFound();

    await jsonDb.SetAsync(cacheKey, "$", product);
    await db.KeyExpireAsync(cacheKey, TimeSpan.FromMinutes(5));

    httpContext.Response.Headers["X-Cache"] = "MISS";
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
    await db.KeyDeleteAsync($"cache:product:{id}");

    return Results.Ok(updated);
});

app.MapDelete("/products/{id}", async (string id, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();

    var removed = await db.KeyDeleteAsync($"product:{id}");
    if (!removed)
        return Results.NotFound();

    await db.SetRemoveAsync("products", id);
    await db.KeyDeleteAsync($"cache:product:{id}");

    return Results.NoContent();
});

app.MapDelete("/cache/{key}", async (string key, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    var removed = await db.KeyDeleteAsync(key);
    if (!removed)
        return Results.NotFound();

    return Results.NoContent();
});

app.MapDefaultEndpoints();

app.Run();
