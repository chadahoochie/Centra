# Centra Sample: Dynamic Noisy Neighbor Tenant Offloading & Fair Scheduling

> Interactive sample showcasing automated multi-tenant traffic monitoring, rolling window rate & latency tracking, noisy neighbor isolation, in-process fair scheduling, broker topic sharding, RabbitMQ pub/sub, .NET Aspire orchestration, and multi-instance competing-consumer consumption with Centra.

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
5. **RabbitMQ & Multi-Instance Competing Consumers**:
   - Multiple replicas of `Centra.Sample.TenantOffload` subscribe to `tenant.orders` using RabbitMQ.
   - Consumers form a competing-consumer group on `centra.pubsub.tenant.orders`, load-balancing event consumption across instances.
   - Each consumer instance tracks handled metrics independently via [`ITenantConsumerNodeState`](Domain/ITenantConsumerNodeState.cs) while enforcing local noisy neighbor rate limits and lane isolation.
6. **Cooldown & Lifecycle Reaping**:
   - Once the burst subsides, [`TenantOffloadReaperHostedService`](../../src/Centra.Hosting/HostedServices/TenantOffloadReaperHostedService.cs) drains idle worker lanes and restores the tenant back to `Normal` state.

---

## ☁️ Running on the .NET Aspire Stack

The sample is fully integrated into [`Centra.AppHost`](../Centra.AppHost):

```csharp
var tenantOffload = builder.AddProject<Projects.Centra_Sample_TenantOffload>("tenant-offload-service")
    .WithCentra(controlPlane)
    .WithCentraRabbitMQ(rabbitmq)
    .WithReplicas(3)
    .WaitFor(rabbitmq)
    .WaitFor(controlPlane);
```

Launch the Aspire AppHost:

```bash
dotnet run --project samples/Centra.AppHost
```

Open the Aspire dashboard to monitor all 3 `tenant-offload-service` replicas, RabbitMQ topic exchange topology, OpenTelemetry traces (`Centra`), and tenant metrics across nodes.

---

## 🚀 Running the Terminal Simulation

Execute the 6-step end-to-end simulation directly from the terminal:

```bash
dotnet run --project samples/Centra.Sample.TenantOffload -- --demo
```

### Or Run as a Web Service

Start a standalone web host:

```bash
dotnet run --project samples/Centra.Sample.TenantOffload
```

Or run multiple instances locally specifying distinct ports and node IDs:

```bash
dotnet run --project samples/Centra.Sample.TenantOffload --urls "http://localhost:5101" --instance-id node-alpha
dotnet run --project samples/Centra.Sample.TenantOffload --urls "http://localhost:5102" --instance-id node-beta
```

---

## 🌐 REST API Endpoints

Each service replica exposes REST endpoints for runtime inspection and traffic generation:

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/` | Root overview with instance ID, broker type, total handled count, and available routes. |
| `GET` | `/instance` | Detailed node status: instance ID, broker type, per-tenant processed counts, and recent handled orders. |
| `GET` | `/tenants` | Real-time sliding window stats and offload status from `ITenantMetricsTracker` and `ITenantOffloadCoordinator`. |
| `POST` | `/orders` | Submit a single order (`TenantId`, `Amount`, `Description`) published as a CloudEvent to `tenant.orders`. |
| `POST` | `/orders/batch` | Dispatch a burst of orders across honest and noisy tenants to test multi-instance distribution. |
| `POST` | `/simulate` | Run the in-process simulation runner via HTTP and return the structured result. |
| `GET` | `/health` | Health check endpoint returning `{ status: "Healthy" }`. |

### Dispatching Test Traffic via cURL

Publish a single order for `tenant-alpha`:

```bash
curl -X POST http://localhost:5101/orders \
  -H "Content-Type: application/json" \
  -d '{"tenantId":"tenant-alpha","amount":250.00,"description":"Order 1"}'
```

Trigger a burst of 60 noisy orders (`tenant-mega`) and 10 honest orders (`tenant-alpha`) across the RabbitMQ cluster:

```bash
curl -X POST http://localhost:5101/orders/batch \
  -H "Content-Type: application/json" \
  -d '{"noisyCount":60,"honestCount":10}'
```

Inspect consumption metrics across each replica:

```bash
curl http://localhost:5101/instance
curl http://localhost:5102/instance
```

---

## 🔬 Simulation Flow & Expected Output

The simulation orchestrates 6 progressive scenarios:

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
6. **Multi-Instance Distributed Consumption**:
   - Simulates multi-replica consumer nodes (`replica-1`, `replica-2`) receiving distributed traffic, tracking independent node metrics while isolating noisy tenant lanes.
