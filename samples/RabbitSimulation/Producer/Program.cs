using Centra.Hosting.Extensions;
using Centra.Providers.RabbitMQ.Extensions;
using Centra.PubSub;
using Centra.Sample.RabbitSimulation.Contracts.Models;
using Centra.Sample.RabbitSimulation.Producer.Services;
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
    .ConfigureResource(resource => resource.AddService(serviceName: "rabbit-producer"))
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

// 1. Core Centra Registration with Attribute-Driven Event Discovery
builder.Services.AddCentra(options =>
{
    options.AppId = "rabbit-producer";
    options.DefaultPubSub = "pubsub";
}, typeof(Program).Assembly, typeof(OrderMessage).Assembly);

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

// 3. Bogus Message Generator and Background Hosted Service
builder.Services.AddSingleton<BogusOrderGenerator>();
builder.Services.AddHostedService<OrderProducerHostedService>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Service = "rabbit-producer",
    Status = "Running",
    Cadence = "1 msg/s",
    Topic = "orders.new"
}));

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }));

app.MapPost("/api/orders/produce", async (
    BogusOrderGenerator generator,
    IPubSubClient pubSubClient,
    CancellationToken ct) =>
{
    var order = generator.Generate();
    await pubSubClient.PublishAsync("orders.new", order, cancellationToken: ct);
    return Results.Accepted($"/api/orders/{order.OrderId}", order);
});

await app.RunAsync();
