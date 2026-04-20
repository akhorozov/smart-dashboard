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

app.MapDelete("/cache/{key}", async (string key, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    var removed = await db.KeyDeleteAsync(key);

    return removed ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/cache/stats", async (IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();

    var hitsRaw = await db.StringGetAsync("cache:hits");
    var missesRaw = await db.StringGetAsync("cache:misses");

    var hits = long.TryParse((string?)hitsRaw, out var parsedHits) ? parsedHits : 0L;
    var misses = long.TryParse((string?)missesRaw, out var parsedMisses) ? parsedMisses : 0L;
    var total = hits + misses;
    var ratio = total == 0 ? 0d : (double)hits / total;

    return Results.Ok(new { hits, misses, ratio });
});

app.MapDefaultEndpoints();
app.Run();
