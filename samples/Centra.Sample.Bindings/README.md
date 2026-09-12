# Centra Distributed Schedulers & Bindings Sample

> An easy-to-understand, zero-dependency sample demonstrating declarative `[CronBinding]` scheduling, multi-instance cluster coordination, inbound webhook triggers, and outbound bindings.

---

## 🧭 Overview

In distributed architectures, scheduled tasks and external system integrations are often difficult to coordinate across multiple application replicas. Without coordination, running multiple instances of a background service causes cron tasks to execute simultaneously on **every node**—leading to duplicate processing, double charging, race conditions, and wasted compute.

Centra solves this natively:
1. **`[CronBinding]` Declarative Attribute**: You define a standard `IJobHandler` and decorate `ExecuteAsync` with a cron expression.
2. **Automatic Multi-Instance Single-Execution**: When running across multiple cluster nodes sharing a distributed lock store (Redis, PostgreSQL, SQL Server, or In-Memory), Centra automatically coordinates execution via `DistributedJobHandler`. **Exactly one node executes each scheduled tick**, while all other nodes safely back off.
3. **`[Binding]` Inbound Webhooks**: Declare HTTP webhook entry points with automatic routing and W3C trace context extraction.
4. **`IOutputBinding`**: Dispatch requests to external systems with built-in resilience and telemetry.

---

## ⏰ 1. Declarative Cron Jobs with `[CronBinding]`

### Defining a Job Handler

To create a scheduled cron job, implement [`IJobHandler`](../../src/Centra.Bindings.Abstractions/IJobHandler.cs) and decorate the `ExecuteAsync` method with [`[CronBinding]`](../../src/Centra.Bindings.Abstractions/CronBindingAttribute.cs):

```csharp
using Centra.Bindings;
using Centra.State;

namespace Centra.Sample.Bindings.Jobs;

public sealed class InventorySnapshotJob : IJobHandler
{
    private readonly IStateStore<InventorySnapshot> _stateStore;

    public InventorySnapshotJob(IStateStore<InventorySnapshot> stateStore)
    {
        _stateStore = stateStore;
    }

    // Runs every 2 seconds on the even second
    [CronBinding("*/2 * * * * *")]
    public async ValueTask ExecuteAsync(ScheduledJobContext context)
    {
        // context.JobName: "InventorySnapshotJob"
        // context.ScheduledTime: The exact target time calculated by the cron schedule
        // context.ActualTime: The time this node began execution (detects latency/jitter)
        // context.Iteration: Incremental tick counter for this job
        // context.CancellationToken: Propagated from graceful application shutdown

        var snapshot = new InventorySnapshot(
            Timestamp: context.ScheduledTime,
            TotalItems: 1500,
            Iteration: context.Iteration,
            ExecutedByNodeId: "node-alpha");

        await _stateStore.SetAsync("current-snapshot", snapshot, cancellationToken: context.CancellationToken);
    }
}
```

### Supported Cron Syntax

Centra features a high-precision, zero-allocation bitmask parser supporting:

| Expression Type | Example | Meaning |
| :--- | :--- | :--- |
| **6-Part Seconds Precision** | `*/2 * * * * *` | Every 2 seconds on the even second |
| **5-Part Standard Cron** | `0 * * * *` | At minute 0 of every hour |
| **5-Part Daily** | `0 2 * * *` | Every day at 02:00 UTC |
| **Interval Descriptors** | `@every 5s` | Every 5 seconds |
| **Frequency Macros** | `@hourly`, `@daily`, `@weekly` | Standard calendar intervals |

---

## 🏢 2. Multi-Instance Single-Execution Coordination

When you deploy your application to Kubernetes, Docker, or Azure App Service with multiple replicas, every instance starts with the identical application code and schedule.

```
                  ┌───────────────────────────────┐
                  │ Shared Distributed Lock Store │
                  │  (Redis / Postgres / Memory)  │
                  └──────────────┬────────────────┘
                                 │
         ┌───────────────────────┼───────────────────────┐
         │ (acquires lock)       │ (lock held -> skips)  │ (lock held -> skips)
         ▼                       ▼                       ▼
   ┌───────────┐           ┌───────────┐           ┌───────────┐
   │Node Alpha │           │ Node Beta │           │Node Gamma │
   │  👑 WON   │           │ ⏭️ SKIPPED │           │ ⏭️ SKIPPED │
   └───────────┘           └───────────┘           └───────────┘
```

### How Centra Guarantees Exactly-Once Execution per Tick

1. **Automatic Wrapping**: During application startup, [`CentraBindingsHostedService`](../../src/Centra.Hosting/HostedServices/CentraBindingsHostedService.cs) detects if an [`IDistributedLockProvider`](../../src/Centra.Locks.Abstractions/IDistributedLockProvider.cs) is registered in DI. If present, it wraps your job in a [`DistributedJobHandler`](../../src/Centra.Bindings/Bindings/DistributedJobHandler.cs).
2. **Timestamped Mutual Exclusion**: When the cron timer triggers, each node calculates the scheduled tick timestamp and attempts to acquire a lock keyed by:
   ```text
   cron:{jobName}:{scheduledUnixTimestamp}
   ```
3. **Winner Executes, Peers Skip**:
   - The first node to acquire the distributed lock executes `ExecuteAsync`.
   - All other nodes discover the lock is already held for that timestamp, log an informative skip notification, and safely exit the tick without throwing or blocking.
4. **Zero Configuration Required**: You do not need to write lock code, coordination loops, or consensus logic. The `[CronBinding]` attribute alone provides full cluster safety.

---

## 📥 3. Inbound Webhook Bindings

Centra allows external systems (payment gateways, external webhooks, IoT devices) to trigger domain handlers via HTTP:

```csharp
using Centra.Bindings;

public sealed class OrdersWebhookTriggerHandler : IBindingTriggerHandler
{
    [Binding("orders-webhook")]
    public async ValueTask<BindingResponse> HandleTriggerAsync(
        BindingData data,
        CancellationToken cancellationToken = default)
    {
        // Centra mounts POST /centra/bindings/orders-webhook automatically
        var payload = data.Data.Span;
        // Process order...

        return new BindingResponse(
            Data: Encoding.UTF8.GetBytes("{\"status\":\"accepted\"}"),
            Metadata: new Dictionary<string, string> { ["content-type"] = "application/json" });
    }
}
```

---

## 📤 4. Outbound Resilient Bindings

Invoke external endpoints or messaging systems through [`IOutputBinding`](../../src/Centra.Bindings.Abstractions/IOutputBinding.cs):

```csharp
var outputBinding = app.Services.GetRequiredService<IOutputBinding>();

var request = new BindingRequest(
    Data: Encoding.UTF8.GetBytes("{\"action\":\"sync\"}"),
    Metadata: new Dictionary<string, string> { ["tenant"] = "acme" });

var response = await outputBinding.InvokeAsync("in-memory-binding", request);
```

---

## 🚀 5. Running the Multi-Node Simulation

This sample includes an automated multi-node simulation that spins up 3 in-memory application nodes (`node-alpha`, `node-beta`, `node-gamma`) and demonstrates cluster coordination:

```bash
dotnet run --project samples/Centra.Sample.Bindings -- --demo
```

### Expected Output

```text
================================================================================
🚀 CENTRA DISTRIBUTED BINDINGS & SCHEDULERS DEMO
   Demonstrating Multi-Instance [CronBinding] Single-Execution,
   Inbound Webhook Triggers, and Resilient Output Bindings
================================================================================

🔧 Initializing shared cluster infrastructure (Distributed Lock Store, State Store)...
📦 Spawning 3 concurrent application nodes: [node-alpha, node-beta, node-gamma]...

--- PHASE 1: Multi-Instance Distributed Cron Single-Execution ---
All 3 nodes are actively running with [CronBinding("*/2 * * * * *")].
Centra wraps each node's job in a DistributedJobHandler using the shared lock store.
Waiting for 2 scheduled cron ticks (every 2s on the even second)...

   ⏭️  [node-beta] SKIPPED (lock already acquired by peer node)
   ⏭️  [node-alpha] SKIPPED (lock already acquired by peer node)
   👑 [node-gamma] WON LOCK -> Executed tick #1 at 20:45:02.000 (Snapshot saved to state store)
   ⏭️  [node-alpha] SKIPPED (lock already acquired by peer node)
   ⏭️  [node-beta] SKIPPED (lock already acquired by peer node)
   👑 [node-gamma] WON LOCK -> Executed tick #2 at 20:45:04.000 (Snapshot saved to state store)

📊 Phase 1 Coordination Metrics:
   * Active Cluster Nodes: 3 [node-alpha, node-beta, node-gamma]
   * Scheduled Ticks Observed: 2
   * Theoretical Uncoordinated Runs: 6 (2 ticks × 3 nodes)
   * Actual Executions Run: 2
   * Redundant Executions Prevented: 4
   * Execution Breakdown per Node:
       - [node-alpha]: 0 win(s), 2 skip(s)
       - [node-beta]: 0 win(s), 2 skip(s)
       - [node-gamma]: 2 win(s), 0 skip(s)
   ✅ Single-Execution Guarantee: VERIFIED (100% mutual exclusion)

--- PHASE 2: Inbound Webhook Binding with Distributed State Propagation ---
Sending external HTTP POST payload to [node-alpha] at '/centra/bindings/orders-webhook'...
   [node-alpha] HTTP Status: OK | Response: {"status":"acknowledged","orderId":"ORD-777"}
Cross-checking state store from [node-beta] to confirm distributed persistence...
   [node-beta] Read order 'ORD-777': Status='ProcessedViaWebhook'
   ✅ Inbound webhook processed on node-alpha and verified on node-beta!

--- PHASE 3: Invoking Outbound Binding from [node-gamma] ---
   [node-gamma] Outbound Binding Echo Response: {"action":"ping","originNode":"node-gamma"}
   ✅ Outbound binding executed successfully!

🛑 Gracefully shutting down all 3 cluster nodes...
================================================================================
🏁 DEMO FINISHED SUCCESSFULLY in 4612 ms!
================================================================================
```
