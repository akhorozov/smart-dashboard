using NRedisStack;
using NRedisStack.DataTypes;
using NRedisStack.Literals.Enums;
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

app.MapPost("/metrics/record", async (MetricRecordRequest metric, IConnectionMultiplexer redis) =>
{
    if (string.IsNullOrWhiteSpace(metric.Name))
        return Results.BadRequest("Metric name is required.");

    var db = redis.GetDatabase();
    var ts = db.TS();
    var timestamp = metric.Timestamp.HasValue ? new TimeStamp(metric.Timestamp.Value) : new TimeStamp("*");
    var addParams = new TsAddParamsBuilder()
        .AddTimestamp(timestamp)
        .AddValue(metric.Value)
        .build();

    var recordedAt = await ts.AddAsync($"metric:{metric.Name}", addParams);

    return Results.Created($"/metrics/{metric.Name}", new
    {
        name = metric.Name,
        timestamp = recordedAt,
        value = metric.Value
    });
});

app.MapGet("/metrics/{name}", async (string name, string? from, string? to, string? aggregation, IConnectionMultiplexer redis) =>
{
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest("Metric name is required.");

    if (!TryParseTimeStamp(from, isFrom: true, out var fromTimestamp))
        return Results.BadRequest("Invalid 'from' value. Use unix timestamp in milliseconds or '-'.");

    if (!TryParseTimeStamp(to, isFrom: false, out var toTimestamp))
        return Results.BadRequest("Invalid 'to' value. Use unix timestamp in milliseconds or '+'.");

    TsAggregation? tsAggregation = null;
    long? timeBucket = null;
    if (!string.IsNullOrWhiteSpace(aggregation))
    {
        if (!TryParseAggregation(aggregation, out var parsedAggregation))
            return Results.BadRequest("Invalid aggregation. Supported values: avg, sum, min, max.");

        if (!long.TryParse(from, out var fromMs) || !long.TryParse(to, out var toMs))
            return Results.BadRequest("When aggregation is provided, both 'from' and 'to' must be unix timestamps in milliseconds.");

        if (toMs < fromMs)
            return Results.BadRequest("'to' must be greater than or equal to 'from'.");

        tsAggregation = parsedAggregation;
        timeBucket = Math.Max(1, toMs - fromMs + 1);
    }

    try
    {
        var db = redis.GetDatabase();
        var points = await db.TS().RangeAsync(
            $"metric:{name}",
            fromTimestamp,
            toTimestamp,
            aggregation: tsAggregation,
            timeBucket: timeBucket);

        return Results.Ok(points.Select(p => new { timestamp = (long)p.Time, value = p.Val }));
    }
    catch (RedisServerException ex) when (ex.Message.Contains("key does not exist", StringComparison.OrdinalIgnoreCase))
    {
        return Results.NotFound();
    }
});

app.MapDefaultEndpoints();

app.Run();

static bool TryParseAggregation(string aggregation, out TsAggregation parsed)
{
    switch (aggregation.Trim().ToLowerInvariant())
    {
        case "avg":
            parsed = TsAggregation.Avg;
            return true;
        case "sum":
            parsed = TsAggregation.Sum;
            return true;
        case "min":
            parsed = TsAggregation.Min;
            return true;
        case "max":
            parsed = TsAggregation.Max;
            return true;
        default:
            parsed = default;
            return false;
    }
}

static bool TryParseTimeStamp(string? raw, bool isFrom, out TimeStamp timestamp)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        timestamp = new TimeStamp(isFrom ? "-" : "+");
        return true;
    }

    var value = raw.Trim();
    if (value is "-" or "+" or "*")
    {
        timestamp = new TimeStamp(value);
        return true;
    }

    if (long.TryParse(value, out var unixTime))
    {
        timestamp = new TimeStamp(unixTime);
        return true;
    }

    timestamp = new TimeStamp(isFrom ? "-" : "+");
    return false;
}

internal sealed record MetricRecordRequest(string Name, double Value, long? Timestamp);
