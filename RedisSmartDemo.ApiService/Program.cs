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

app.MapGet("/metrics/{name}", (string name) =>
{
    var metricName = name.ToLowerInvariant();
    if (metricName is not ("cpu" or "temperature" or "latency" or "events"))
    {
        return Results.NotFound();
    }

    var now = DateTime.UtcNow;
    var start = now.AddMinutes(-5);
    var random = new Random();
    var points = new List<MetricPoint>(61);

    for (var i = 0; i <= 60; i++)
    {
        var timestamp = start.AddSeconds(i * 5);
        var wave = Math.Sin(i / 7d);
        var value = metricName switch
        {
            "cpu" => Math.Clamp(55 + wave * 25 + random.NextDouble() * 10, 0, 100),
            "temperature" => Math.Clamp(62 + wave * 8 + random.NextDouble() * 2, 45, 90),
            "latency" => Math.Clamp(120 + wave * 35 + random.NextDouble() * 15, 20, 400),
            "events" => Math.Clamp(220 + wave * 70 + random.NextDouble() * 20, 0, 1200),
            _ => 0
        };

        points.Add(new MetricPoint(timestamp, Math.Round((decimal)value, 2)));
    }

    return Results.Ok(points);
});

app.MapDefaultEndpoints();

app.Run();

internal sealed record MetricPoint(DateTime Timestamp, decimal Value);
