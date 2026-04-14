using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis")
    .WithImageRegistry("docker.io")
    .WithImage("redis/redis-stack")
    .WithImageTag("latest")
    .WithArgs(
        "--loadmodule", "/opt/redis-stack/lib/rejson.so",
        "--loadmodule", "/opt/redis-stack/lib/redisearch.so",
        "--loadmodule", "/opt/redis-stack/lib/redistimeseries.so",
        "--loadmodule", "/opt/redis-stack/lib/redisbloom.so");


var apiService = builder.AddProject<Projects.RedisSmartDemo_ApiService>("apiservice")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(redis)
     .WaitFor(redis);

builder.AddProject<Projects.RedisSmartDemo_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
