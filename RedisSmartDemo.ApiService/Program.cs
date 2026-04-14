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

app.MapDefaultEndpoints();
app.Run();

