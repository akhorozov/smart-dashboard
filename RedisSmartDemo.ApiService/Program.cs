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

app.MapPost("/bloom/check-email", async (EmailRequest request, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    var exists = await db.BF().ExistsAsync("bloom:emails", request.Email);

    return Results.Ok(new { exists });
});

app.MapPost("/bloom/register-email", async (EmailRequest request, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    await db.BF().AddAsync("bloom:emails", request.Email);

    return Results.Ok();
});

app.MapPost("/bloom/check-product-view", async (ProductViewRequest request, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    var exists = await db.BF().ExistsAsync($"bloom:user:{request.UserId}:seen", request.ProductId);

    return Results.Ok(new { exists });
});

app.MapPost("/bloom/record-product-view", async (ProductViewRequest request, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    await db.BF().AddAsync($"bloom:user:{request.UserId}:seen", request.ProductId);

    return Results.Ok();
});

app.MapDefaultEndpoints();

app.Run();

public record EmailRequest(string Email);

public record ProductViewRequest(string UserId, string ProductId);
