using Centra.Events;
using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Providers.RabbitMQ.Extensions;
using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Centra.Sample.TenantOffload.Domain;
using Centra.Sample.TenantOffload.Simulation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

if (args.Contains("--demo", StringComparer.OrdinalIgnoreCase))
{
    var simResult = await TenantOffloadDemoRunner.RunAsync(args);
    return simResult.AllStepsSucceeded ? 0 : 1;
}

var builder = WebApplication.CreateBuilder(args);

// Multi-instance identification: instance ID configured via CLI, config, or generated
var instanceId = builder.Configuration["instance-id"]
    ?? builder.Configuration["Centra:ControlPlane:InstanceId"]
    ?? Environment.GetEnvironmentVariable("HOSTNAME")
    ?? $"node-{Guid.NewGuid():N}"[..8];

var rabbitConnectionString = builder.Configuration.GetConnectionString("rabbitmq")
    ?? builder.Configuration["Centra:RabbitMQ:ConnectionString"];
var rabbitHostName = builder.Configuration["RabbitMQ:HostName"];

var brokerType = (!string.IsNullOrWhiteSpace(rabbitConnectionString) || !string.IsNullOrWhiteSpace(rabbitHostName))
    ? "RabbitMQ"
    : "InMemory";

// 1. Configure Centra Distributed Framework
builder.Services.AddCentra(options =>
{
    options.AppId = "tenant-offload-service";
    options.DefaultPubSub = "pubsub";
    options.ControlPlane.InstanceId = instanceId;
}, typeof(TenantOrderEventHandler).Assembly);

// 2. Configure Pub/Sub Provider (RabbitMQ if connection string or host is present, otherwise In-Memory)
if (!string.IsNullOrWhiteSpace(rabbitConnectionString))
{
    builder.Services.AddCentraRabbitMQPubSub("pubsub", options =>
    {
        options.ConnectionString = rabbitConnectionString;
    });
}
else if (!string.IsNullOrWhiteSpace(rabbitHostName))
{
    builder.Services.AddCentraRabbitMQPubSub("pubsub", options =>
    {
        options.HostName = rabbitHostName;
        options.Port = int.TryParse(builder.Configuration["RabbitMQ:Port"], out var p) ? p : 5672;
        options.UserName = builder.Configuration["RabbitMQ:UserName"] ?? "guest";
        options.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
    });
}
else
{
    builder.Services.AddCentraInMemory();
}

// 3. Dynamic Noisy Neighbor Tenant Offloading Engine
builder.Services.AddCentraTenantOffload(options =>
{
    options.WindowDuration = TimeSpan.FromSeconds(10);
    options.MinSampleCount = 10;
    options.TrafficShareThreshold = 0.50;
    options.DurationMultiplierThreshold = 2.5;
    options.CooldownPeriod = TimeSpan.FromSeconds(5);
    options.OffloadStrategy = TenantOffloadStrategyType.EphemeralBrokerTopic;
    options.MaxConcurrencyPerTenant = 2;
    options.PerTenantQueueCapacity = 200;
    options.LaneIdleTimeout = TimeSpan.FromSeconds(30);
    options.OffloadTopicPattern = "{topic}.offload.{tenantId}";
    options.EnablePublisherBypassing = false;
});

// 4. Multi-Instance Consumer Node State Tracker
builder.Services.AddSingleton<ITenantConsumerNodeState>(new TenantConsumerNodeState(instanceId, brokerType));

var app = builder.Build();

// 5. REST Endpoints for Multi-Instance Monitoring and Load Generation
app.MapGet("/", (HttpContext context, ITenantConsumerNodeState nodeState) =>
{
    if (context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Content(TenantOffloadDashboardHtml.Render(nodeState), "text/html");
    }

    return Results.Ok(new
    {
        Application = "Centra.Sample.TenantOffload",
        InstanceId = nodeState.InstanceId,
        Broker = nodeState.BrokerType,
        Status = "Running",
        TotalHandledOnNode = nodeState.TotalHandled,
        TenantHandledCounts = nodeState.TenantHandledCounts,
        Endpoints = new[]
        {
            "GET /instance",
            "GET /tenants",
            "POST /orders",
            "POST /orders/batch",
            "GET/POST /simulate[?hold={seconds}]",
            "GET /health"
        }
    });
});

app.MapGet("/instance", (ITenantConsumerNodeState nodeState) => Results.Ok(new
{
    nodeState.InstanceId,
    Broker = nodeState.BrokerType,
    nodeState.TotalHandled,
    nodeState.TenantHandledCounts,
    RecentOrders = nodeState.RecentProcessedOrders
}));

app.MapGet("/tenants", (ITenantMetricsTracker tracker, ITenantOffloadCoordinator coordinator) =>
{
    var tenantIds = new[] { "tenant-alpha", "tenant-beta", "tenant-gamma", "tenant-mega" };
    var stats = tenantIds.Select(tenantId =>
    {
        var tStats = tracker.GetTenantStats(tenantId, "tenant.orders");
        var isOffloaded = coordinator.IsTenantOffloaded(tenantId, "tenant.orders", out var reason);
        return new
        {
            TenantId = tenantId,
            MessageCount = tStats.MessageCount,
            TrafficShareRatio = tStats.TrafficShareRatio,
            AverageDurationMs = tStats.AverageDurationMs,
            IsOffloaded = isOffloaded,
            OffloadReason = reason.ToString()
        };
    });
    return Results.Ok(stats);
});

app.MapPost("/orders", async (
    CreateTenantOrderRequest request,
    IPubSubClient pubSub,
    ITenantConsumerNodeState nodeState,
    CancellationToken ct) =>
{
    var orderId = $"ord-{request.TenantId}-{Guid.NewGuid():N}"[..18];
    var evt = new TenantOrderEvent(orderId, request.TenantId, request.Amount, request.Description ?? $"Order {orderId}");
    var options = new PubSubPublishOptions
    {
        Metadata = new Dictionary<string, string>
        {
            [CloudEventConstants.TenantIdHeader] = request.TenantId
        }
    };

    await pubSub.PublishAsync("tenant.orders", evt, options, ct);
    return Results.Accepted($"/orders/{orderId}", new
    {
        OrderId = orderId,
        TenantId = request.TenantId,
        Amount = request.Amount,
        DispatchedByInstance = nodeState.InstanceId,
        Status = "Queued"
    });
});

app.MapPost("/orders/batch", async (
    BatchOrdersRequest? request,
    IPubSubClient pubSub,
    ITenantConsumerNodeState nodeState,
    CancellationToken ct) =>
{
    var req = request ?? new BatchOrdersRequest();
    var noisyCount = req.NoisyCount;
    var honestCount = req.HonestCount;

    for (var i = 1; i <= honestCount; i++)
    {
        var alphaOrder = new TenantOrderEvent($"ord-alpha-{Guid.NewGuid():N}"[..18], "tenant-alpha", 150m, $"Honest order #{i}");
        await pubSub.PublishAsync("tenant.orders", alphaOrder, new PubSubPublishOptions
        {
            Metadata = new Dictionary<string, string> { [CloudEventConstants.TenantIdHeader] = "tenant-alpha" }
        }, ct);
    }

    for (var i = 1; i <= noisyCount; i++)
    {
        var megaOrder = new TenantOrderEvent($"ord-mega-{Guid.NewGuid():N}"[..18], "tenant-mega", 9.99m, $"Burst order #{i}");
        await pubSub.PublishAsync("tenant.orders", megaOrder, new PubSubPublishOptions
        {
            Metadata = new Dictionary<string, string> { [CloudEventConstants.TenantIdHeader] = "tenant-mega" }
        }, ct);
    }

    return Results.Accepted("/orders/batch", new
    {
        DispatchedBy = nodeState.InstanceId,
        HonestOrdersDispatched = honestCount,
        NoisyOrdersDispatched = noisyCount,
        Topic = "tenant.orders",
        Message = "Batch orders dispatched across cluster."
    });
});

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }));

app.MapMethods("/simulate", ["GET", "POST"], async (int? hold, CancellationToken ct) =>
{
    var holdSeconds = hold ?? 0;
    var result = await TenantOffloadDemoRunner.RunWithServicesAsync(app.Services, ct, holdSeconds);
    return Results.Ok(result);
});

app.MapCentraEndpoints();

await app.RunAsync();
return 0;

public partial class Program { }
