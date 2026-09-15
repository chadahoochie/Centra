# Centra Sample: Dynamic Noisy Neighbor Tenant Offloading & Fair Scheduling

> Interactive simulation showcasing automated multi-tenant traffic monitoring, rolling window rate & latency tracking, noisy neighbor isolation, in-process fair scheduling, broker topic sharding, and state recovery with Centra.

---

## 🎯 What This Sample Demonstrates

In multi-tenant event-driven systems, an unconstrained surge from a single tenant ("noisy neighbor") can saturate consumer thread pools, exhaust broker queues, and starve well-behaved tenants of compute capacity.

Centra solves this architecturally through **Dynamic Tenant Offloading**:

1. **Continuous Rolling Window Metrics**:
   - [`ITenantMetricsTracker`](../../src/Centra.PubSub.Abstractions/Tenancy/ITenantMetricsTracker.cs) tracks per-tenant message rates, traffic share ratios, and execution latencies over sliding evaluation windows without heap allocations.
2. **Automated Anomaly Detection & State Machine**:
   - When a tenant exceeds configurable traffic share thresholds (`TrafficShareThreshold`) or execution duration thresholds (`DurationMultiplierThreshold`), [`ITenantOffloadCoordinator`](../../src/Centra.PubSub.Abstractions/Tenancy/ITenantOffloadCoordinator.cs) transitions the tenant from `Normal` to `Offloaded`.
3. **Pluggable Offload Strategies**:
   - **`InProcessFairScheduler`**: Routes offloaded tenant events into an isolated, dedicated [`TenantWorkerLane`](../../src/Centra.PubSub/Tenancy/TenantWorkerLane.cs) with bounded concurrency and queue capacity, preserving unblocked execution for honest tenants.
   - **`BoundedShardBrokerTopic`**: Hashes offloaded tenants across a fixed pool of broker topics (`{topic}.offload.{shard}`), distributing partition load.
   - **`EphemeralBrokerTopic`**: Dynamically creates dedicated ephemeral broker topics with auto-deletion and TTL.
4. **Publish-Side Steering & Inbound Interception**:
   - [`CentraPubSubClient`](../../src/Centra.PubSub/PubSub/CentraPubSubClient.cs) dynamically resolves outbound target topics via `ResolvePublishTopic`.
   - [`CentraSubscriptionEventDispatcher`](../../src/Centra.Hosting/HostedServices/CentraSubscriptionEventDispatcher.cs) intercepts incoming messages from offloaded tenants, delegating execution to the offload coordinator.
5. **Cooldown & Lifecycle Reaping**:
   - Once the burst subsides, [`TenantOffloadReaperHostedService`](../../src/Centra.Hosting/HostedServices/TenantOffloadReaperHostedService.cs) drains idle worker lanes and restores the tenant back to `Normal` state.

---

## 🚀 Running the Simulation

Execute the interactive 5-step simulation directly from the terminal:

```bash
dotnet run --project samples/Centra.Sample.TenantOffload -- --demo
```

### Or Run as a Web Service

Start the web host:

```bash
dotnet run --project samples/Centra.Sample.TenantOffload
```

Then trigger the simulation via HTTP POST:

```bash
curl -X POST http://localhost:5000/simulate
```

---

## 🔬 Simulation Flow & Expected Output

The demo orchestrates 5 progressive scenarios:

1. **Baseline Multi-Tenant Traffic**:
   - Three honest tenants (`tenant-alpha`, `tenant-beta`, `tenant-gamma`) send standard orders.
   - Traffic shares remain balanced (~33%), and all tenants stay in `Normal` state.
2. **Noisy Neighbor Surge**:
   - `tenant-mega` floods the topic with a rapid batch of orders, spiking above 50% traffic share.
   - The coordinator detects `TenantOffloadReason.HighTrafficShare` and marks `tenant-mega` as `Offloaded`.
3. **Fair Scheduling & Tenant Isolation**:
   - While `tenant-mega`'s backlog is queued in a dedicated worker lane (`MaxConcurrencyPerTenant = 2`), `tenant-alpha`'s concurrent orders process immediately on the primary subscription pipeline without starvation.
4. **Broker Topic Sharding Demonstration**:
   - Shows how `BoundedShardBrokerTopicOffloadStrategy` and `EphemeralBrokerTopicOffloadStrategy` deterministically hash tenants to isolated broker topics (`tenant.orders.offload.0`, etc.).
5. **Cooldown, State Recovery & Lane Reaping**:
   - After the noisy surge stops, the cooldown window elapses, returning `tenant-mega` to `Normal` state and reaping idle worker lane resources.
