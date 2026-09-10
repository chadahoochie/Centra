using Centra.Hosting.Extensions;
using Centra.Providers.RabbitMQ.Extensions;
using Centra.Sample.RabbitSimulation.Consumer.Handlers;
using Centra.Sample.RabbitSimulation.Contracts.Clients;
using Centra.Sample.RabbitSimulation.Contracts.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry: exports Centra's ActivitySource ("Centra") and Meter ("Centra"),
// plus ASP.NET Core, HTTP, and runtime instrumentation over OTLP to Aspire Dashboard.
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "rabbit-consumer"))
    .WithTracing(tracing => tracing
        .AddSource("Centra")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter("Centra")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation())
    .UseOtlpExporter();

// 1. Core Centra Registration with Attribute-Only Configuration
// Auto-discovers [Topic]-decorated event handlers (OrderSubmittedEventHandler) and
// [ServiceClient]-decorated RPC client proxies (IOrderApiClient) via assembly scanning.
builder.Services.AddCentra(options =>
{
    options.AppId = "rabbit-consumer";
    options.DefaultPubSub = "pubsub";
}, typeof(Program).Assembly, typeof(IOrderApiClient).Assembly);

// 2. RabbitMQ Pub/Sub Driver
builder.Services.AddCentraRabbitMQPubSub("pubsub", options =>
{
    var connectionString = builder.Configuration.GetConnectionString("rabbitmq");
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.ConnectionString = connectionString;
    }
    else
    {
        options.HostName = builder.Configuration["RabbitMQ:HostName"] ?? "localhost";
        options.Port = int.TryParse(builder.Configuration["RabbitMQ:Port"], out var p) ? p : 5672;
        options.UserName = builder.Configuration["RabbitMQ:UserName"] ?? "guest";
        options.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
    }
});

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Service = "rabbit-consumer",
    Status = "Running",
    SubscribedTopic = "orders.new",
    TargetRpcService = "rabbit-api"
}));

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }));

await app.RunAsync();
