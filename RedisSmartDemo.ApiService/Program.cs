using NRedisStack;
using NRedisStack.RedisStackCommands;
using RedisSmartDemo.Api.Models;
using Scalar.AspNetCore;
using StackExchange.Redis;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

builder.AddRedisClient("redis");

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();
var cacheTtl = TimeSpan.FromSeconds(60);

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
    var cacheKey = $"cache:user:{id}";
    var cached = await db.StringGetAsync(cacheKey);
    if (cached.HasValue)
    {
        await db.StringIncrementAsync("cache:hits");
        httpContext.Response.Headers["X-Cache"] = "HIT";
        return Results.Content(cached.ToString()!, "application/json");
    }

    await db.StringIncrementAsync("cache:misses");
    httpContext.Response.Headers["X-Cache"] = "MISS";

    var jsonDb = db.JSON();
    var user = await jsonDb.GetAsync<User>($"user:{id}");
    if (user is null)
        return Results.NotFound();

    var serialized = JsonSerializer.Serialize(user);
    await db.StringSetAsync(cacheKey, serialized, cacheTtl);

    return Results.Ok(user);
});

app.MapGet("/products/{id}", async (string id, IConnectionMultiplexer redis, HttpContext httpContext) =>
{
    var db = redis.GetDatabase();
    var cacheKey = $"cache:product:{id}";
    var cached = await db.StringGetAsync(cacheKey);
    if (cached.HasValue)
    {
        await db.StringIncrementAsync("cache:hits");
        httpContext.Response.Headers["X-Cache"] = "HIT";
        return Results.Content(cached.ToString()!, "application/json");
    }

    await db.StringIncrementAsync("cache:misses");
    httpContext.Response.Headers["X-Cache"] = "MISS";

    var jsonDb = db.JSON();
    var product = await jsonDb.GetAsync<Product>($"product:{id}");
    if (product is null)
        return Results.NotFound();

    var serialized = JsonSerializer.Serialize(product);
    await db.StringSetAsync(cacheKey, serialized, cacheTtl);

    return Results.Ok(product);
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

app.MapDefaultEndpoints();

app.Run();
