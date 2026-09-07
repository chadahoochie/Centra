using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Sample.Actors.Domain;
using Centra.Sample.Actors.Simulation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

// If requested via CLI flag or running without args, execute simulation
if (args.Contains("--demo", StringComparer.OrdinalIgnoreCase) || args.Length == 0)
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
builder.Services.AddCentraInMemory();
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
