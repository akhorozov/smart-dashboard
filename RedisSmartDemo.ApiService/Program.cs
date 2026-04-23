using RedisSmartDemo.Api.Endpoints;
using RedisSmartDemo.Api.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.AddRedisClient("redis");
builder.Services.AddOpenApi();

builder.Services.AddHostedService<SearchIndexInitializer>();
builder.Services.AddHostedService<MetricsBackgroundService>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapUserEndpoints();
app.MapProductEndpoints();
app.MapMetricEndpoints();
app.MapActivityEndpoints();
app.MapRecommendationEndpoints();
app.MapAdminEndpoints();
app.MapCacheEndpoints();

app.MapGet("/ratelimit/check/{userId}", async (string userId, IConnectionMultiplexer redis) =>
{
    const int limit = 10;
    const int windowSeconds = 60;

    if (string.IsNullOrWhiteSpace(userId) ||
        userId.Any(ch => !char.IsLetterOrDigit(ch) && ch != '-' && ch != '_'))
    {
        return Results.BadRequest(new { error = "Invalid userId format." });
    }

    var db = redis.GetDatabase();
    var key = $"ratelimit:{userId}";

    var count = (long)await db.ScriptEvaluateAsync(
        "local current = redis.call('INCR', KEYS[1]) " +
        "if redis.call('TTL', KEYS[1]) <= 0 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end " +
        "return current",
        [key],
        [windowSeconds]);

    var ttl = await db.KeyTimeToLiveAsync(key) ?? TimeSpan.Zero;
    var resetInSeconds = Math.Max(0, (int)Math.Ceiling(ttl.TotalSeconds));
    var remaining = Math.Max(0, limit - (int)count);
    var allowed = count <= limit;

    return Results.Ok(new { remaining, limit, resetInSeconds, allowed });
});

app.MapDefaultEndpoints();
app.Run();
