using Centra.Actors;
using Centra.Core.Actors;
using Centra.Hosting.Extensions;
using Centra.Hosting.Options;
using Centra.Invocation;
using Centra.Locks;
using Centra.Providers.RabbitMQ.Extensions;
using Centra.Providers.Redis.Extensions;
using Centra.PubSub;
using Centra.Sample.DockerStack.Cluster;
using Centra.Sample.DockerStack.Cron;
using Centra.Sample.DockerStack.Domain;
using Centra.Sample.DockerStack.Handlers;
using Centra.Sample.DockerStack.Services;
using Centra.State;
using Centra.Sync;
using Centra.Workflows;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// docker-compose.yml sets each container's `hostname:` to its service name (node-1/node-2/node-3),
// which Environment.MachineName reflects on Linux containers - this doubles as the replica's
// unique cluster InstanceId and its DNS-reachable address.
var instanceId = builder.Configuration["instance-id"]
    ?? Environment.MachineName;

const string appId = "dockerstack-node";
var nodeAddress = builder.Configuration["Centra:NodeAddress"] ?? $"http://{instanceId}:8080";
var controlPlaneEndpoint = builder.Configuration["Centra:ControlPlaneEndpoint"];
var redisConnectionString = builder.Configuration.GetConnectionString("redis") ?? "localhost:6379";

// 0. OpenTelemetry: exports Centra's baked-in ActivitySource ("Centra") and Meter ("Centra") -
// see Centra.Diagnostics.CentraDiagnostics/CentraMeters - over OTLP to the collector, which fans
// traces out to Tempo, metrics to Prometheus, and logs to Loki (see samples/DockerStack/otel).
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: appId, serviceInstanceId: instanceId))
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

// 1. Core Centra registration: actors, workflows, bindings, invocation, and control-plane
// heartbeat/sync are all wired up here (all replicas share AppId "dockerstack-node" so that
// round-robin service invocation treats them as one logical service).
builder.Services.AddCentra(options =>
{
    options.AppId = appId;
    options.ControlPlaneEndpoint = controlPlaneEndpoint;
    options.ControlPlane.InstanceId = instanceId;
    options.ControlPlane.Metadata["address"] = nodeAddress;
});

// 2. Providers: Redis backs shared state + distributed locks, RabbitMQ backs pub/sub.
builder.Services.AddCentraRedisStateStore("statestore", o => o.ConnectionString = redisConnectionString);
builder.Services.AddCentraRedisLocks("lockstore", o => o.ConnectionString = redisConnectionString);

builder.Services.AddCentraRabbitMQPubSub("pubsub", o =>
{
    o.HostName = builder.Configuration["RabbitMQ:HostName"] ?? "localhost";
    o.UserName = builder.Configuration["RabbitMQ:UserName"] ?? "guest";
    o.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
});

// 3. Actors: register the demo actor, then re-point placement/proxying at this replica's unique
// InstanceId instead of the shared AppId (see Cluster/PeerInstanceEndpointResolver.cs and
// Cluster/PeerActorRingSync.cs for why - AddCentraActors alone would leave every replica thinking
// it's the only node).
builder.Services.AddActor<CounterActor, ICounterActor>();

builder.Services.AddSingleton<ConsistentHashRing>(_ =>
{
    var ring = new ConsistentHashRing();
    ring.AddNode(instanceId);
    return ring;
});
builder.Services.AddSingleton<IActorPlacementDirector>(sp =>
    new ActorPlacementDirector(instanceId, sp.GetRequiredService<ConsistentHashRing>()));
builder.Services.AddSingleton<IActorProxyFactory>(sp =>
{
    var manager = sp.GetRequiredService<ActorManager>();
    var placement = sp.GetRequiredService<IActorPlacementDirector>();
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var resolver = new PeerInstanceEndpointResolver(sp.GetRequiredService<IControlPlaneClient>());
    var invoker = new CentraServiceInvoker(httpClientFactory.CreateClient("actor-peer"), endpointResolver: resolver);
    return new ActorProxyFactory(manager, placement, invoker);
});
builder.Services.AddHostedService<PeerActorRingSync>();

// 4. Workflows / sagas.
builder.Services.AddWorkflow<OrderProcessingWorkflow>();
builder.Services.AddWorkflowActivity<ValidateOrderActivity>();
builder.Services.AddWorkflowActivity<ReserveInventoryActivity>();
builder.Services.AddWorkflowActivity<ReleaseInventoryCompensationActivity>();
builder.Services.AddWorkflowActivity<ProcessPaymentActivity>();
builder.Services.AddWorkflowActivity<ShipOrderActivity>();

// 5. Cron binding: registered identically on every replica; Centra's DistributedJobHandler wraps
// it with the shared Redis lock store so only one replica executes any given scheduled tick.
builder.Services.AddCentraCronJob<ClusterHeartbeatJob>(ClusterHeartbeatJob.JobName, "*/10 * * * * *");

// 6. Peer service invocation (round-robin across replicas) + node-local demo state + CloudEvents handler.
builder.Services.AddSingleton<IClusterNodeLocalState, ClusterNodeLocalState>();
builder.Services.AddCentraServiceClient<IPeerClient>();
builder.Services.AddCentraEventHandler<ClusterTaskEventHandler, ClusterTaskEvent>(
    pubSubName: "pubsub",
    topic: "cluster.tasks");

var app = builder.Build();

app.MapGet("/", (IClusterNodeLocalState nodeState) => Results.Ok(new
{
    Message = "Centra DockerStack Cluster Node",
    NodeId = nodeState.InstanceId,
    AppId = nodeState.AppId,
    IsLeader = nodeState.IsLeader,
    Endpoints = new[]
    {
        "GET /instance",
        "GET /peers/info",
        "POST /leader/acquire",
        "POST /leader/release",
        "GET /state/counter",
        "POST /state/counter/increment",
        "POST /tasks/dispatch",
        "GET /tasks",
        "GET /cron/last-run",
        "POST /actors/{id}/increment",
        "GET /actors/{id}",
        "POST /centra/workflows/OrderProcessingWorkflow/start",
        "GET /centra/workflows/{instanceId}"
    }
}));

app.MapGet("/instance", (HttpContext ctx, IClusterNodeLocalState nodeState) =>
{
    var hostAddress = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    return Results.Ok(nodeState.GetNodeInfo(hostAddress));
});

// --- Service Invocation: round-robin RPC across peer replicas via Control Plane topology ---
app.MapGet("/peers/info", async (IPeerClient peerClient) =>
{
    var nodeInfo = await peerClient.GetNodeInfoAsync();
    return Results.Ok(nodeInfo);
});

// --- Redis Distributed Lock: mutual exclusion / leader election ---
app.MapPost("/leader/acquire", async (
    int? leaseSeconds,
    IDistributedLockProvider lockProvider,
    IClusterNodeLocalState nodeState,
    IOptions<CentraOptions> options,
    CancellationToken ct) =>
{
    var duration = TimeSpan.FromSeconds(leaseSeconds ?? 15);
    var existing = nodeState.GetLeaderLock();
    if (existing is not null)
    {
        return Results.Ok(new AcquireLeaderResponse(true, nodeState.InstanceId, "Node already holds leadership lease.", duration));
    }

    var @lock = await lockProvider.TryAcquireLockAsync(
        options.Value.DefaultLockStore,
        "primary-leader",
        duration,
        ct);

    if (@lock is null)
    {
        return Results.Conflict(new AcquireLeaderResponse(false, nodeState.InstanceId, "Leadership lease held by another instance.", null));
    }

    nodeState.SetLeaderLock(@lock);
    return Results.Ok(new AcquireLeaderResponse(true, nodeState.InstanceId, "Successfully acquired leadership lock.", duration));
});

app.MapPost("/leader/release", async (IClusterNodeLocalState nodeState, CancellationToken ct) =>
{
    var @lock = nodeState.GetLeaderLock();
    if (@lock is null)
    {
        return Results.BadRequest(new { Released = false, Message = "No active leadership lock held by this node." });
    }

    await @lock.DisposeAsync();
    nodeState.SetLeaderLock(null);
    return Results.Ok(new { Released = true, Message = "Leadership released successfully." });
});

// --- Redis State Store: optimistic concurrency with ETags ---
app.MapGet("/state/counter", async (IStateStore<ClusterCounterState> stateStore, CancellationToken ct) =>
{
    var entry = await stateStore.GetAsync("shared-job-counter", cancellationToken: ct);
    return entry.HasValue
        ? Results.Ok(new { entry.Value.Value.Counter, entry.Value.Value.LastUpdatedByInstanceId, entry.Value.Value.LastUpdatedAtUtc, entry.Value.ETag })
        : Results.Ok(new { Counter = 0L, LastUpdatedByInstanceId = "none", LastUpdatedAtUtc = (DateTimeOffset?)null, ETag = (string?)null });
});

app.MapPost("/state/counter/increment", async (
    IncrementCounterRequest request,
    IStateStore<ClusterCounterState> stateStore,
    IClusterNodeLocalState nodeState,
    CancellationToken ct) =>
{
    const int maxRetries = 3;
    var attempts = 0;

    while (attempts < maxRetries)
    {
        attempts++;
        var entry = await stateStore.GetAsync("shared-job-counter", cancellationToken: ct);
        var currentVal = entry.HasValue ? entry.Value.Value.Counter : 0;
        var currentETag = entry.HasValue ? entry.Value.ETag : null;

        var nextVal = currentVal + request.IncrementBy;
        var newState = new ClusterCounterState(nextVal, nodeState.InstanceId, DateTimeOffset.UtcNow);

        var targetETag = (request.SimulateConflict && attempts == 1) ? "stale-simulated-etag" : currentETag;

        bool updated;
        if (targetETag is null)
        {
            await stateStore.SetAsync("shared-job-counter", newState, cancellationToken: ct);
            updated = true;
        }
        else
        {
            updated = await stateStore.TrySetAsync("shared-job-counter", newState, targetETag, cancellationToken: ct);
        }

        if (updated)
        {
            var refreshed = await stateStore.GetAsync("shared-job-counter", cancellationToken: ct);
            return Results.Ok(new IncrementCounterResponse(
                Success: true,
                Value: nextVal,
                ETag: refreshed.HasValue ? refreshed.Value.ETag : null,
                RetryAttempts: attempts,
                Message: $"Counter updated to {nextVal} by {nodeState.InstanceId}."));
        }

        await Task.Delay(Random.Shared.Next(10, 40), ct);
    }

    return Results.Conflict(new IncrementCounterResponse(
        Success: false,
        Value: -1,
        ETag: null,
        RetryAttempts: attempts,
        Message: "Failed to update counter due to concurrency conflicts."));
});

// --- RabbitMQ Pub/Sub: CNCF CloudEvents delivered cluster-wide ---
app.MapPost("/tasks/dispatch", async (
    DispatchTaskRequest request,
    IPubSubClient pubSub,
    IClusterNodeLocalState nodeState,
    CancellationToken ct) =>
{
    var evt = new ClusterTaskEvent(
        TaskId: $"task-{Guid.NewGuid():N}"[..12],
        TaskType: request.TaskType,
        AssignedByInstanceId: nodeState.InstanceId,
        Payload: request.Payload,
        CreatedAtUtc: DateTimeOffset.UtcNow);

    await pubSub.PublishAsync("pubsub", "cluster.tasks", evt, cancellationToken: ct);
    return Results.Accepted("/tasks", evt);
});

app.MapGet("/tasks", (IClusterNodeLocalState nodeState) => Results.Ok(nodeState.ProcessedTasks));

// --- Cron Bindings: proves only one replica executes each scheduled tick ---
app.MapGet("/cron/last-run", async (IStateStore<CronLastRunState> stateStore, CancellationToken ct) =>
{
    var entry = await stateStore.GetAsync(ClusterHeartbeatJob.LastRunStateKey, cancellationToken: ct);
    return entry.HasValue ? Results.Ok(entry.Value.Value) : Results.Ok(new { Message = "Cron job has not executed yet." });
});

// --- Actors: proves virtual actors are spread across cluster nodes, with transparent remote proxying ---
app.MapPost("/actors/{id}/increment", async (string id, IActorProxyFactory proxyFactory, IActorPlacementDirector placement) =>
{
    var identity = new ActorIdentity(new ActorType("CounterActor"), new ActorId(id));
    var ownerNodeId = placement.ResolveNodeId(identity);

    var proxy = proxyFactory.CreateActorProxy<ICounterActor>(new ActorId(id));
    var value = await proxy.IncrementAsync();

    return Results.Ok(new { ActorId = id, Value = value, HandledByInstanceId = ownerNodeId });
});

app.MapGet("/actors/{id}", async (string id, IActorProxyFactory proxyFactory, IActorPlacementDirector placement) =>
{
    var identity = new ActorIdentity(new ActorType("CounterActor"), new ActorId(id));
    var ownerNodeId = placement.ResolveNodeId(identity);

    var proxy = proxyFactory.CreateActorProxy<ICounterActor>(new ActorId(id));
    var value = await proxy.GetValueAsync();

    return Results.Ok(new { ActorId = id, Value = value, HandledByInstanceId = ownerNodeId });
});

// 7. Mount Centra actor invocation, workflow (start/status/history), and CloudEvents/bindings routes.
app.MapCentraEndpoints();

app.Run();

public partial class Program { }
