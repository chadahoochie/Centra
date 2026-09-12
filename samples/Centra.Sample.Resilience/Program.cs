using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Resilience;
using Centra.Sample.Resilience.Domain;
using Centra.Sample.Resilience.Simulation;

// If explicitly requested via CLI flag, execute simulation
if (args.Contains("--demo", StringComparer.OrdinalIgnoreCase))
{
    var simResult = await ResilienceDemoRunner.RunAsync(args);
    return simResult.BaselineSuccess && simResult.TransientRetrySuccess && simResult.CircuitBreakerTripped ? 0 : 1;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCentra(opt =>
{
    opt.AppId = "resilience-sample-app";
});
builder.Services.AddCentraInMemory();
builder.Services.AddCentraResilience(opt =>
{
    opt.AddPolicy(new CentraResiliencePolicyDefinition(
        PolicyName: "default-invocation",
        Retry: new RetryPolicyOptions(MaxRetries: 3),
        CircuitBreaker: new CircuitBreakerPolicyOptions(FailureRatio: 0.5, BreakDuration: TimeSpan.FromSeconds(5)),
        Timeout: new TimeoutPolicyOptions(TimeSpan.FromSeconds(5))));
});

builder.Services.AddCentraServiceClient<IPaymentGatewayClient>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Application = "Centra.Sample.Resilience",
    Status = "Running",
    Documentation = "Run with --demo flag to execute interactive fault injection simulation."
}));

app.MapPost("/simulate", async () =>
{
    var result = await ResilienceDemoRunner.RunAsync();
    return Results.Ok(result);
});

await app.RunAsync();
return 0;
