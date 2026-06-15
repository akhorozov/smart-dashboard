using RedisSmartDemo.Api.Endpoints;
using RedisSmartDemo.Api.Services;
using Scalar.AspNetCore;

const string ActivityChannelName = "activity";

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

app.MapPost("/activity/publish", async (ActivityPublishRequest request, IConnectionMultiplexer redis) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
        return Results.BadRequest("Message is required.");

    var activityChannel = RedisChannel.Literal(ActivityChannelName);
    var subscriber = redis.GetSubscriber();
    await subscriber.PublishAsync(activityChannel, request.Message);

    return Results.Accepted();
});

app.MapGet("/activity/stream", async (HttpContext context, IConnectionMultiplexer redis) =>
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    var activityChannel = RedisChannel.Literal(ActivityChannelName);
    var subscriber = redis.GetSubscriber();
    ChannelMessageQueue? queue = null;

    try
    {
        queue = await subscriber.SubscribeAsync(activityChannel);

        while (true)
        {
            if (context.RequestAborted.IsCancellationRequested)
                break;

            ChannelMessage message;
            try
            {
                message = await queue.ReadAsync(context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                break;
            }

            foreach (var line in message.Message.ToString().Replace("\r", "").Split('\n'))
            {
                await context.Response.WriteAsync($"data: {line}\n", context.RequestAborted);
            }
            await context.Response.WriteAsync("\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
    finally
    {
        if (queue != null)
            await queue.UnsubscribeAsync();
    }
});

app.MapDefaultEndpoints();
app.Run();

public record ActivityPublishRequest(string Message);
