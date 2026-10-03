using Centra.Hosting.Extensions;
using Centra.Providers.Flotilla.Grpc.Extensions;
using Centra.Providers.Flotilla.Tcp.Extensions;
using Centra.Providers.Flotilla.Udp.Extensions;
using Centra.Sample.FlotillaSimulation.Contracts.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var transport = builder.Configuration["Centra:Flotilla:Transport"] ?? "tcp";
var serviceName = $"flotilla-consumer-{transport.ToLowerInvariant()}";

// OpenTelemetry Setup
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: serviceName))
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
builder.Services.AddCentra(options =>
{
    options.AppId = serviceName;
    options.DefaultPubSub = "pubsub";
}, typeof(Program).Assembly, typeof(ITelemetryApiClient).Assembly);

// 2. Flotilla Pub/Sub Driver based on selected transport
var configuredNode = builder.Configuration["Centra:Flotilla:ClusterNodes:0"] ?? builder.Configuration["Centra:Flotilla:ClusterNodes"];
if (string.Equals(transport, "grpc", StringComparison.OrdinalIgnoreCase))
{
    var node = !string.IsNullOrWhiteSpace(configuredNode) ? configuredNode : "http://localhost:9300";
    builder.Services.AddCentraFlotillaGrpc(options =>
    {
        options.DefaultPubSubName = "pubsub";
        options.ClusterNodes = [node];
    });
}
else if (string.Equals(transport, "udp", StringComparison.OrdinalIgnoreCase))
{
    var node = !string.IsNullOrWhiteSpace(configuredNode) ? configuredNode : "127.0.0.1:9200";
    builder.Services.AddCentraFlotillaUdp(options =>
    {
        options.DefaultPubSubName = "pubsub";
        options.ClusterNodes = [node];
    });
}
else
{
    var node = !string.IsNullOrWhiteSpace(configuredNode) ? configuredNode : "127.0.0.1:9100";
    builder.Services.AddCentraFlotillaTcp(options =>
    {
        options.DefaultPubSubName = "pubsub";
        options.ClusterNodes = [node];
    });
}

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Service = serviceName,
    Status = "Running",
    Transport = transport,
    SubscribedTopic = "telemetry.v1",
    TargetRpcService = "flotilla-api"
}));

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }));

await app.RunAsync();
