using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Sample.Actors.Domain;
using Centra.Sample.Actors.Simulation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using Centra.Providers.Redis.Extensions;

// If requested via CLI flag, execute interactive simulation
if (args.Contains("--demo", StringComparer.OrdinalIgnoreCase))
{
    var simResult = await ActorDemoRunner.RunAsync(args);
    return simResult.ConcurrentDepositsCount == 50 && simResult.PassivationAndReactivationSucceeded && simResult.ReminderExecuted ? 0 : 1;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCentra(options =>
{
    options.AppId = "actors-sample-app";
    options.DefaultStateStore = "statestore";
});

var redisConnectionString = builder.Configuration.GetConnectionString("redis")
    ?? builder.Configuration["Centra:Redis:ConnectionString"];

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddCentraRedisStateStore("statestore", o => o.ConnectionString = redisConnectionString);
    builder.Services.AddCentraRedisLocks("statestore", o => o.ConnectionString = redisConnectionString);
}
else
{
    builder.Services.AddCentraInMemory();
}
builder.Services.AddCentraActors(options =>
{
    options.DefaultStateStore = "statestore";
    options.ActorIdleTimeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddActor<AccountActor, IAccountActor>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Application = "Centra.Sample.Actors",
    Status = "Running",
    Documentation = "Run with --demo flag to execute interactive virtual actors simulation."
}));

app.MapCentraActorEndpoints();

app.MapPost("/simulate", async () =>
{
    var result = await ActorDemoRunner.RunAsync();
    return Results.Ok(result);
});

await app.RunAsync();
return 0;
