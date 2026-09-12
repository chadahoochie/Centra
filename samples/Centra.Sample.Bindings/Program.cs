using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Providers.Redis.Extensions;
using Centra.Sample.Bindings.Jobs;
using Centra.Sample.Bindings.Simulation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

if (args.Contains("--demo", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("Centra Distributed Application Framework - Schedulers & Bindings");
    await BindingsDemoRunner.RunAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCentra(options =>
{
    options.AppId = "bindings-sample-app";
    options.DefaultStateStore = "statestore";
    options.DefaultLockStore = "lockstore";
}, typeof(InventorySnapshotJob).Assembly);

var redisConnectionString = builder.Configuration.GetConnectionString("redis")
    ?? builder.Configuration["Centra:Redis:ConnectionString"];

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddCentraRedisStateStore("statestore", o => o.ConnectionString = redisConnectionString);
    builder.Services.AddCentraRedisLocks("lockstore", o => o.ConnectionString = redisConnectionString);
}
else
{
    builder.Services.AddCentraInMemory();
}

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Application = "Centra.Sample.Bindings",
    Status = "Running",
    Documentation = "Run with --demo flag to execute interactive schedulers and bindings simulation."
}));

app.MapCentraEndpoints();

app.MapPost("/simulate", async () =>
{
    var result = await BindingsDemoRunner.RunAsync();
    return Results.Ok(result);
});

await app.RunAsync();
