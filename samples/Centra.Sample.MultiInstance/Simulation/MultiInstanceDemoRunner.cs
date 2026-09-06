using System.Diagnostics;
using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using Centra.ControlPlane.Topology;
using Centra.Drivers;
using Centra.Events;
using Centra.Hosting.Extensions;
using Centra.Invocation;
using Centra.Locks;
using Centra.Providers.InMemory.Bindings;
using Centra.Providers.InMemory.Extensions;
using Centra.Providers.InMemory.Locks;
using Centra.Providers.InMemory.PubSub;
using Centra.Providers.InMemory.State;
using Centra.PubSub;
using Centra.Registry;
using Centra.Sample.MultiInstance.Domain;
using Centra.Sample.MultiInstance.Handlers;
using Centra.Sample.MultiInstance.Services;
using Centra.State;
using Centra.Sync;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.MultiInstance.Simulation;

public static class MultiInstanceDemoRunner
{
    public static async Task<SimulationResult> RunAsync(
        string[]? args = null,
        CancellationToken cancellationToken = default)
    {
        var logs = new List<string>();
        void Log(string message)
        {
            var line = $"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}";
            logs.Add(line);
            Console.WriteLine(line);
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine(" CENTRA DISTRIBUTED APPLICATION FRAMEWORK - NATIVE SERVICE DISCOVERY & PEERS    ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        var stopwatch = Stopwatch.StartNew();

        // 1. Start In-Process Control Plane
        Log("Initializing Centra Control Plane...");
        var cpBuilder = WebApplication.CreateBuilder();
        cpBuilder.WebHost.UseTestServer();
        cpBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
        cpBuilder.Services.AddCentraControlPlane();

        var cpApp = cpBuilder.Build();
        cpApp.MapCentraControlPlaneEndpoints();
        await cpApp.StartAsync(cancellationToken).ConfigureAwait(false);
        var cpClient = cpApp.GetTestServer().CreateClient();
        var topologyTracker = cpApp.Services.GetRequiredService<ITopologyTracker>();

        // 2. Shared Distributed Drivers (simulating Redis/distributed cluster backing store)
        var sharedStateDriver = new InMemoryStateStoreDriver();
        var sharedLockDriver = new InMemoryDistributedLockDriver();
        var sharedPubSubDriver = new InMemoryPubSubDriver();
        var sharedBindingDriver = new InMemoryBindingDriver();

        // 3. Build 3 Peer Node Hosts
        var nodeIds = new[] { "node-alpha", "node-beta", "node-gamma" };
        var hosts = new List<IHost>();

        foreach (var nodeId in nodeIds)
        {
            Log($"Configuring Peer Node '{nodeId}'...");
            var host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
                .ConfigureServices(services =>
                {
                    services.AddCentra(options =>
                    {
                        options.AppId = "multi-instance-service";
                        options.DefaultStateStore = "cluster-statestore";
                        options.DefaultPubSub = "cluster-pubsub";
                        options.DefaultLockStore = "cluster-lockstore";
                        options.ControlPlaneEndpoint = "http://localhost/";
                        options.ControlPlane.InstanceId = nodeId;
                        options.ControlPlane.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
                        options.ControlPlane.Metadata["address"] = $"http://localhost:510{Array.IndexOf(nodeIds, nodeId) + 1}/";
                    });

                    // Shared distributed drivers
                    services.AddSingleton(sharedStateDriver);
                    services.AddSingleton(sharedLockDriver);
                    services.AddSingleton(sharedPubSubDriver);
                    services.AddSingleton(sharedBindingDriver);

                    services.AddSingleton<IStateStoreDriver>(sp => sp.GetRequiredService<InMemoryStateStoreDriver>());
                    services.AddSingleton<IPubSubDriver>(sp => sp.GetRequiredService<InMemoryPubSubDriver>());
                    services.AddSingleton<IDistributedLockDriver>(sp => sp.GetRequiredService<InMemoryDistributedLockDriver>());
                    services.AddSingleton<IBindingDriver>(sp => sp.GetRequiredService<InMemoryBindingDriver>());

                    services.AddSingleton<IComponentInitializer>(sp => new InMemoryComponentInitializer(
                        sp.GetRequiredService<InMemoryStateStoreDriver>(),
                        sp.GetRequiredService<InMemoryPubSubDriver>(),
                        sp.GetRequiredService<InMemoryDistributedLockDriver>(),
                        sp.GetRequiredService<InMemoryBindingDriver>(),
                        defaultStateStore: "cluster-statestore",
                        defaultPubSub: "cluster-pubsub",
                        defaultLockStore: "cluster-lockstore"));

                    // Point Control Plane client to the in-process TestServer
                    services.AddSingleton<IControlPlaneClient>(new ControlPlaneClient(cpClient));

                    // Node state, typed service client, and CloudEvent handlers
                    services.AddSingleton<IClusterNodeLocalState, ClusterNodeLocalState>();
                    services.AddCentraServiceClient<INodePeerClient>();
                    services.AddCentraEventHandler<ClusterTaskEventHandler, ClusterTaskEvent>(
                        pubSubName: "cluster-pubsub",
                        topic: "cluster.tasks");
                })
                .Build();

            hosts.Add(host);
        }

        // 4. Start all 3 nodes
        foreach (var host in hosts)
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        Log("All 3 peer nodes started. Waiting for initial heartbeats to register with Control Plane...");
        await Task.Delay(250, cancellationToken).ConfigureAwait(false);

        // --- PHASE 1: NATIVE FRAMEWORK SERVICE DISCOVERY & TOPOLOGY ---
        Console.ForegroundColor = ConsoleColor.Green;
        Log("\n--- PHASE 1: Native Framework Service Discovery & Topology Tracking ---");
        Console.ResetColor();

        var resolver = hosts[0].Services.GetRequiredService<IServiceEndpointResolver>();
        var resolvedUri1 = await resolver.ResolveEndpointAsync("multi-instance-service", cancellationToken).ConfigureAwait(false);
        var resolvedUri2 = await resolver.ResolveEndpointAsync("multi-instance-service", cancellationToken).ConfigureAwait(false);
        Log($"Native IServiceEndpointResolver resolved 'multi-instance-service' -> Round 1: {resolvedUri1} | Round 2: {resolvedUri2}");

        var activeNodes = await topologyTracker.GetActiveNodesAsync(cancellationToken).ConfigureAwait(false);
        Log($"Control Plane registered {activeNodes.Count} active peer instance(s) reporting heartbeats:");
        foreach (var node in activeNodes)
        {
            var address = node.Metadata is not null && node.Metadata.TryGetValue("address", out var addr) ? addr : "none";
            Log($"  * Peer: [{node.InstanceId}] | Status: {node.Status} | Address: {address} | LastHeartbeat: {node.LastHeartbeatUtc:HH:mm:ss.fff}");
        }

        // --- PHASE 2: DISTRIBUTED MUTUAL EXCLUSION (LEADER ELECTION) ---
        Console.ForegroundColor = ConsoleColor.Green;
        Log("\n--- PHASE 2: Distributed Mutual Exclusion & Leader Failover ---");
        Console.ResetColor();

        var lockA = hosts[0].Services.GetRequiredService<IDistributedLockProvider>();
        var lockB = hosts[1].Services.GetRequiredService<IDistributedLockProvider>();
        var lockC = hosts[2].Services.GetRequiredService<IDistributedLockProvider>();

        Log("All 3 peer nodes simultaneously competing for distributed lock 'primary-leader'...");
        var lockTasks = new[]
        {
            lockA.TryAcquireLockAsync("cluster-lockstore", "primary-leader", TimeSpan.FromSeconds(5), cancellationToken).AsTask(),
            lockB.TryAcquireLockAsync("cluster-lockstore", "primary-leader", TimeSpan.FromSeconds(5), cancellationToken).AsTask(),
            lockC.TryAcquireLockAsync("cluster-lockstore", "primary-leader", TimeSpan.FromSeconds(5), cancellationToken).AsTask()
        };

        var locks = await Task.WhenAll(lockTasks).ConfigureAwait(false);
        var winnerIndex = Array.FindIndex(locks, l => l is not null);
        var leaderLock = winnerIndex >= 0 ? locks[winnerIndex] : null;
        var leaderNodeId = winnerIndex >= 0 ? nodeIds[winnerIndex] : null;

        Log($"Leader election result: Winner is [{leaderNodeId}]");
        for (var i = 0; i < locks.Length; i++)
        {
            Log($"  * Peer [{nodeIds[i]}]: Acquired = {locks[i] is not null}");
        }

        // Leader performs critical work, then voluntarily releases lock
        Log($"Leader [{leaderNodeId}] executing exclusive work, then yielding lease...");
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        if (leaderLock is not null)
        {
            await leaderLock.DisposeAsync().ConfigureAwait(false);
        }

        // Standby node takes over
        var standbyLockProvider = winnerIndex == 0 ? lockB : lockA;
        var standbyNodeId = winnerIndex == 0 ? "node-beta" : "node-alpha";

        Log($"Standby peer [{standbyNodeId}] acquiring leadership after handoff...");
        var failoverLock = await standbyLockProvider.TryAcquireLockAsync("cluster-lockstore", "primary-leader", TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        Log($"Failover result: [{standbyNodeId}] Acquired = {failoverLock is not null}");
        if (failoverLock is not null)
        {
            await failoverLock.DisposeAsync().ConfigureAwait(false);
        }

        // --- PHASE 3: DISTRIBUTED STATE & OPTIMISTIC CONCURRENCY (ETAGS) ---
        Console.ForegroundColor = ConsoleColor.Green;
        Log("\n--- PHASE 3: Shared Distributed State & Optimistic Concurrency Control (ETags) ---");
        Console.ResetColor();

        var stateStoreA = hosts[0].Services.GetRequiredService<IStateStore<ClusterCounterState>>();
        var stateStoreB = hosts[1].Services.GetRequiredService<IStateStore<ClusterCounterState>>();
        var stateStoreC = hosts[2].Services.GetRequiredService<IStateStore<ClusterCounterState>>();

        Log("Node [node-alpha] initializing shared counter to 100...");
        await stateStoreA.SetAsync("shared-job-counter", new ClusterCounterState(100, "node-alpha", DateTimeOffset.UtcNow), cancellationToken: cancellationToken).ConfigureAwait(false);

        Log("Node [node-beta] attempting write with stale ETag to test conflict detection...");
        var conflictRejected = !await stateStoreB.TrySetAsync("shared-job-counter", new ClusterCounterState(105, "node-beta", DateTimeOffset.UtcNow), "stale-simulated-etag", cancellationToken: cancellationToken).ConfigureAwait(false);
        Log($"Conflict detected as expected: {conflictRejected}");

        Log("Node [node-beta] reading latest ETag and retrying write...");
        var entryB = await stateStoreB.GetAsync("shared-job-counter", cancellationToken: cancellationToken).ConfigureAwait(false);
        var retried = entryB.HasValue && await stateStoreB.TrySetAsync("shared-job-counter", new ClusterCounterState(105, "node-beta", DateTimeOffset.UtcNow), entryB.Value.ETag!, cancellationToken: cancellationToken).ConfigureAwait(false);
        Log($"Retry with fresh ETag succeeded: {retried}");

        Log("Node [node-gamma] incrementing counter to 115...");
        var entryC = await stateStoreC.GetAsync("shared-job-counter", cancellationToken: cancellationToken).ConfigureAwait(false);
        if (entryC.HasValue)
        {
            await stateStoreC.TrySetAsync("shared-job-counter", new ClusterCounterState(115, "node-gamma", DateTimeOffset.UtcNow), entryC.Value.ETag!, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var finalEntry = await stateStoreA.GetAsync("shared-job-counter", cancellationToken: cancellationToken).ConfigureAwait(false);
        var finalCounter = finalEntry.HasValue ? finalEntry.Value.Value.Counter : 0;
        var lastUpdatedBy = finalEntry.HasValue ? finalEntry.Value.Value.LastUpdatedByInstanceId : "unknown";
        Log($"Final verified shared counter value across cluster: {finalCounter} (Last updated by: {lastUpdatedBy})");

        // --- PHASE 4: CNCF CLOUDEVENTS DISTRIBUTED PUB/SUB ---
        Console.ForegroundColor = ConsoleColor.Green;
        Log("\n--- PHASE 4: CNCF CloudEvents Distributed Pub/Sub Messaging ---");
        Console.ResetColor();

        CentraAmbientContext.CorrelationId = $"sim-corr-{Guid.NewGuid():N}"[..16];
        CentraAmbientContext.TenantId = "tenant-enterprise-1";

        var pubSubA = hosts[0].Services.GetRequiredService<IPubSubClient>();
        var taskEvent = new ClusterTaskEvent(
            TaskId: $"task-{Guid.NewGuid():N}"[..12],
            TaskType: "DataSyncBatchJob",
            AssignedByInstanceId: "node-alpha",
            Payload: "{\"batchId\":\"batch-771\",\"records\":5000}",
            CreatedAtUtc: DateTimeOffset.UtcNow);

        Log($"Dispatched CloudEvent from [node-alpha] with Ambient CorrelationId: {CentraAmbientContext.CorrelationId}...");
        await pubSubA.PublishAsync("cluster.tasks", taskEvent, cancellationToken: cancellationToken).ConfigureAwait(false);

        Log($"Task {taskEvent.TaskId} published to topic 'cluster.tasks'. Waiting for subscribers...");
        await Task.Delay(250, cancellationToken).ConfigureAwait(false);

        var totalTasksProcessed = 0;
        for (var i = 0; i < hosts.Count; i++)
        {
            var nodeState = hosts[i].Services.GetRequiredService<IClusterNodeLocalState>();
            var processed = nodeState.ProcessedTasks;
            totalTasksProcessed += processed.Count;
            Log($"  * Peer [{nodeIds[i]}]: Processed {processed.Count} task(s). Details: {string.Join(", ", processed.Select(p => p.TaskId))}");
        }

        // --- PHASE 5: SUMMARY & CLEANUP ---
        Console.ForegroundColor = ConsoleColor.Green;
        Log("\n--- PHASE 5: Simulation Completed Successfully ---");
        Console.ResetColor();

        stopwatch.Stop();
        Log($"Total simulation elapsed time: {stopwatch.ElapsedMilliseconds} ms");

        foreach (var host in hosts)
        {
            await host.StopAsync(cancellationToken).ConfigureAwait(false);
            host.Dispose();
        }
        await cpApp.StopAsync(cancellationToken).ConfigureAwait(false);
        await cpApp.DisposeAsync().ConfigureAwait(false);

        return new SimulationResult(
            ClusterName: "centra-multi-instance-cluster",
            NodeCount: nodeIds.Length,
            LeaderElectionSuccessful: leaderLock is not null,
            ElectedLeaderInstanceId: leaderNodeId,
            ConcurrencyConflictResolved: conflictRejected && retried,
            FinalCounterValue: finalCounter,
            TotalTasksProcessed: totalTasksProcessed,
            ElapsedDuration: stopwatch.Elapsed,
            LogEntries: logs);
    }
}
