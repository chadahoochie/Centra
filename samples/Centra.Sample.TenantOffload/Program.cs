using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.PubSub.Tenancy;
using Centra.Sample.TenantOffload.Domain;
using Centra.Sample.TenantOffload.Simulation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

if (args.Contains("--demo", StringComparer.OrdinalIgnoreCase))
{
    var simResult = await TenantOffloadDemoRunner.RunAsync(args);
    return simResult.AllStepsSucceeded ? 0 : 1;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCentra(options =>
{
    options.AppId = "tenant-offload-sample-app";
    options.DefaultPubSub = "pubsub";
}, typeof(TenantOrderEventHandler).Assembly);

builder.Services.AddCentraInMemory();

builder.Services.AddCentraTenantOffload(options =>
{
    options.WindowDuration = TimeSpan.FromSeconds(10);
    options.MinSampleCount = 10;
    options.TrafficShareThreshold = 0.50;
    options.DurationMultiplierThreshold = 2.5;
    options.CooldownPeriod = TimeSpan.FromSeconds(5);
    options.OffloadStrategy = TenantOffloadStrategyType.InProcessFairScheduler;
    options.MaxConcurrencyPerTenant = 2;
    options.PerTenantQueueCapacity = 200;
    options.LaneIdleTimeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Application = "Centra.Sample.TenantOffload",
    Status = "Running",
    Documentation = "Run with --demo flag to execute interactive dynamic noisy neighbor tenant offloading simulation."
}));

app.MapCentraEndpoints();

app.MapPost("/simulate", async () =>
{
    var result = await TenantOffloadDemoRunner.RunAsync();
    return Results.Ok(result);
});

await app.RunAsync();
return 0;
