using Centra.Hosting.Extensions;
using Centra.Hosting.Options;
using Centra.Locks;
using Centra.Providers.InMemory.Extensions;
using Centra.Providers.Redis.Extensions;
using Centra.PubSub;
using Centra.Sample.MultiInstance.Domain;
using Centra.Sample.MultiInstance.Handlers;
using Centra.Sample.MultiInstance.Services;
using Centra.Sample.MultiInstance.Simulation;
using Centra.State;
using Microsoft.Extensions.Options;

// If explicitly requested via CLI flag, execute interactive multi-node simulation
if (args.Contains("--demo", StringComparer.OrdinalIgnoreCase))
{
    var simResult = await MultiInstanceDemoRunner.RunAsync(args);
    return simResult.LeaderElectionSuccessful && simResult.ConcurrencyConflictResolved ? 0 : 1;
}

var builder = WebApplication.CreateBuilder(args);

// Extract InstanceId from CLI arg --instance-id, or config, or generate
var instanceId = builder.Configuration["instance-id"]
    ?? builder.Configuration["Centra:ControlPlane:InstanceId"]
    ?? $"node-{Guid.NewGuid():N}"[..12];

var appId = builder.Configuration["Centra:AppId"] ?? "multi-instance-service";

// 1. Configure Centra Distributed Framework
builder.Services.AddCentra(options =>
{
    options.AppId = appId;
    options.DefaultStateStore = "cluster-statestore";
    options.DefaultPubSub = "cluster-pubsub";
    options.DefaultLockStore = "cluster-lockstore";
    options.ControlPlane.InstanceId = instanceId;
});

// 2. Configure Providers (Redis if configured, otherwise zero-dependency In-Memory)
var redisConnectionString = builder.Configuration.GetConnectionString("redis")
    ?? builder.Configuration["Centra:Redis:ConnectionString"];

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddCentraRedis(options =>
    {
        options.ConnectionString = redisConnectionString;
    });
}
else
{
    builder.Services.AddCentraInMemory(
        defaultStateStore: "cluster-statestore",
        defaultPubSub: "cluster-pubsub",
        defaultLockStore: "cluster-lockstore");
}

// 3. Register Node-Local State, Typed RPC Client, and CloudEvent Handlers
builder.Services.AddSingleton<IClusterNodeLocalState, ClusterNodeLocalState>();
builder.Services.AddCentraServiceClient<INodePeerClient>();
builder.Services.AddCentraEventHandler<ClusterTaskEventHandler, ClusterTaskEvent>(
    pubSubName: "cluster-pubsub",
    topic: "cluster.tasks");

var app = builder.Build();

// 4. REST Endpoints - Pure Domain Code (Code Focused on Code)
app.MapGet("/", (IClusterNodeLocalState nodeState) => Results.Ok(new
{
    Message = "Centra Multi-Instance Peer Node",
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
        "POST /demo"
    }
}));

app.MapGet("/instance", (HttpContext ctx, IClusterNodeLocalState nodeState) =>
{
    var hostAddress = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    return Results.Ok(nodeState.GetNodeInfo(hostAddress));
});

// --- Native Framework Service Discovery RPC Invocation ---
app.MapGet("/peers/info", async (INodePeerClient peerClient) =>
{
    // Native framework service discovery & load balancing under the hood:
    // Centra resolves "multi-instance-service" from Control Plane topology,
    // load balances across healthy replicas round-robin, and proxies the call!
    var nodeInfo = await peerClient.GetNodeInfoAsync();
    return Results.Ok(nodeInfo);
});

// --- Distributed Locking: Mutual Exclusion & Leader Election ---
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

app.MapPost("/leader/release", async (
    IClusterNodeLocalState nodeState,
    CancellationToken ct) =>
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

// --- Shared State: Optimistic Concurrency with ETags ---
app.MapGet("/state/counter", async (
    IStateStore<ClusterCounterState> stateStore,
    CancellationToken ct) =>
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
        long currentVal = entry.HasValue ? entry.Value.Value.Counter : 0;
        string? currentETag = entry.HasValue ? entry.Value.ETag : null;

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

// --- CNCF CloudEvents: Distributed Pub/Sub Messaging ---
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

    await pubSub.PublishAsync("cluster.tasks", evt, cancellationToken: ct);
    return Results.Accepted("/tasks", evt);
});

app.MapGet("/tasks", (IClusterNodeLocalState nodeState) =>
{
    return Results.Ok(nodeState.ProcessedTasks);
});

// --- Simulation Trigger ---
app.MapPost("/demo", async (CancellationToken ct) =>
{
    var result = await MultiInstanceDemoRunner.RunAsync(cancellationToken: ct);
    return Results.Ok(result);
});

// 5. Mount Centra CloudEvents & Bindings route dispatcher
app.MapCentraEndpoints();

app.Run();
return 0;

public partial class Program { }
