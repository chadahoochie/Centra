using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Sample.Workflows.Domain;
using Centra.Sample.Workflows.Simulation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using Centra.Providers.Redis.Extensions;

// If requested via CLI flag, execute simulation
if (args.Contains("--demo", StringComparer.OrdinalIgnoreCase))
{
    var simResult = await WorkflowDemoRunner.RunAsync(args);
    return simResult.HappyPathCompleted && simResult.SagaRollbackSucceeded && simResult.ExternalApprovalSucceeded ? 0 : 1;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCentra(options =>
{
    options.AppId = "workflows-sample-app";
    options.DefaultStateStore = "statestore";
    options.DefaultLockStore = "lockstore";
});

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
builder.Services.AddCentraWorkflows(options =>
{
    options.DefaultStateStore = "statestore";
    options.DefaultLockStore = "lockstore";
});

// Register sample workflows and activities
builder.Services.AddWorkflow<OrderProcessingWorkflow>();
builder.Services.AddWorkflow<ManagerApprovalWorkflow>();

builder.Services.AddWorkflowActivity<ValidateOrderActivity>();
builder.Services.AddWorkflowActivity<ReserveInventoryActivity>();
builder.Services.AddWorkflowActivity<ReleaseInventoryCompensationActivity>();
builder.Services.AddWorkflowActivity<ProcessPaymentActivity>();
builder.Services.AddWorkflowActivity<ShipOrderActivity>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Application = "Centra.Sample.Workflows",
    Status = "Running",
    Documentation = "Run with --demo flag to execute interactive distributed workflows simulation."
}));

app.MapCentraWorkflowEndpoints();

app.MapPost("/simulate", async () =>
{
    var result = await WorkflowDemoRunner.RunAsync();
    return Results.Ok(result);
});

await app.RunAsync();
return 0;
