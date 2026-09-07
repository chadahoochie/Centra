# Centra: Distributed Application Framework for .NET 10

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512bd4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-351%20Passed-brightgreen.svg)]()

> A high-performance, cloud-native distributed application framework for .NET 10 engineered natively in C# to deliver zero-sidecar in-process speed, unify component governance with **Centralized Component Management**, ensure **Observability is a core tenant**, standardize messaging on **CNCF CloudEvents v1.0**, integrate enterprise **Distributed Resilience & Fault Tolerance** powered by Polly Core v8, orchestrate durable **Distributed Workflows & Sagas** with deterministic replay and automated LIFO compensations, host stateful **Distributed Virtual Actors**, and allow developers to write pure business logic where **"code is focused on code"**.

---

## 🏛️ Key Architectural Pillars

1. **Native In-Process Performance (Zero Sidecars)**:
   - Eliminates sidecar loopback HTTP/gRPC serialization hops and process overhead.
   - High-throughput in-process pipeline using `ValueTask`, `readonly record struct`, `ReadOnlyMemory<byte>`, and `ArrayPool<byte>`.
2. **Modular & Pluggable Abstractions (Zero Dependency Drag)**:
   - Fine-grained, decoupled contracts following Interface Segregation Principle (ISP).
   - Consume *only* what you need (e.g. `Centra.PubSub.Abstractions` or `Centra.Resilience.Abstractions` without dragging State Store or Distributed Locks).
   - Composable DI registrations (`AddCentraPubSub`, `AddCentraState`, `AddCentraLocks`, `AddCentraInvocation`, `AddCentraBindings`, `AddCentraResilience`, `AddCentraActors`, `AddCentraWorkflows`, or full-stack `AddCentra`).
3. **Distributed Resilience & Fault Tolerance Pipeline**:
   - Zero-allocation execution engine wrapping **Polly Core v8** with composite pipelines: **Timeout -> Bulkhead / Concurrency Limiter -> Rate Limiter -> Circuit Breaker -> Retry**.
   - Exponential, linear, or constant backoff with jitter and cancellation token propagation.
   - Automatic circuit state machine transitions (`Closed` -> `Open` -> `HalfOpen` -> `Closed`) with microsecond fast-fail rejection under sustained outages.
   - Live, zero-downtime policy hot-reloading pushed centrally from the Centra Control Plane over Server-Sent Events (SSE).
4. **Centralized Component Management**:
   - Eliminates fragmented, desynchronized YAML component files and Kubernetes CRD drift.
   - Components (State Stores, Pub/Sub Brokers, Distributed Locks, Bindings, Resilience Policies, and Workflows) are managed, versioned, and monitored centrally with real-time SSE hot-reloading.
5. **Observability as a Core Tenant**:
   - **Distributed Tracing**: Built directly on `System.Diagnostics.ActivitySource("Centra", "1.0.0")` with W3C `DistributedContextPropagator` context propagation (`traceparent`, `tracestate`).
   - **Semantic Span Roles**: Proper `ActivityKind` assignment (`Producer` on pub, `Consumer` on sub, `Client` on RPC invoke, `Server` on RPC handle, `Internal` on state/locks/workflows).
   - **Standard Metrics**: Built on `System.Diagnostics.Metrics.Meter("Centra", "1.0.0")` reporting operation counters, latency histograms, resilience retry/circuit breaker instruments, and active gauges.
   - **Zero-Allocation Logging**: High-performance `[LoggerMessage]` source generators.
6. **CNCF CloudEvents v1.0 Standard**:
   - Transparently wraps and unwraps domain records into CloudEvents v1.0.
   - Supports **Binary Mode** (default zero-allocation: raw body + `ce-*` headers) and **Structured Mode** (single JSON document).
   - Automatically tracks enterprise extensions: `ce-correlationid`, `ce-causationid`, `ce-tenantid`, `ce-schemaversion`.
7. **Code Focused on Code**:
   - Domain developers interact with clean, strongly typed interfaces (`IStateStore<T>`, `IPubSubClient`, `IDistributedLockProvider`, `IResiliencePipelineProvider`, typed RPC clients, `IActorProxyFactory`, `IWorkflowClient`) without vendor plumbing.
8. **Distributed Virtual Actors Runtime**:
   - High-throughput virtual actors with turn-based sequential single-threaded execution (zero race conditions).
   - Consistent hash ring partition placement (`ConsistentHashRing`) across cluster nodes with virtual vnodes.
   - Dynamic client RPC proxy generation (`IActorProxyFactory`, `DispatchProxy`).
   - Optimistic concurrency state management (`IActorStateManager`, ETag CAS) with dirty-tracking and automatic turn-based commits.
   - Ephemeral timers (`IActorTimerManager`) and durable reminders (`IActorReminderManager`, `IRemindable`) with distributed lock coordination across cluster replicas.
   - Automatic activation lifecycle with async deduplication, idle timeout passivation, and clean shutdown.
9. **Distributed Workflows & Sagas (Durable Task Orchestration Engine)**:
   - Durable multi-step workflow orchestrations with deterministic event-sourced replay.
   - First-class distributed sagas with automated LIFO compensation rollbacks on activity failures or cancellations.
   - Durable timers for reliable, crash-resilient asynchronous delays without holding open worker threads.
   - External event awaits via CloudEvents for human-in-the-loop approvals and asynchronous inter-service coordination.
   - In-process execution with zero sidecars and zero-allocation performance conventions.

---

## 🚀 Quickstart: Code Focused on Code

### 1. Register Centra in ASP.NET Core (`Program.cs`)

You can register the complete framework or only specific building blocks:

```csharp
var builder = WebApplication.CreateBuilder(args);

// OPTION A: Full-Stack Registration (All Building Blocks)
builder.Services.AddCentra(options =>
{
    options.AppId = "orders-service";
    options.DefaultStateStore = "statestore";
    options.DefaultPubSub = "pubsub";
    options.DefaultLockStore = "lockstore";
});

// OPTION B: Composable Modular Registration (Only what you need!)
// builder.Services.AddCentraPubSub();
// builder.Services.AddCentraState();
// builder.Services.AddCentraLocks();

// Add Providers:
// Option 1: In-Memory Provider (zero-dependency local dev & testing)
builder.Services.AddCentraInMemory();

// Option 2: Production Distributed Providers
// builder.Services.AddCentraRedis(options => options.Configuration = "localhost:6379");
// builder.Services.AddCentraPostgreSql(options => options.ConnectionString = "Host=localhost;Database=centra;...");
// builder.Services.AddCentraRabbitMQ(options => options.HostName = "localhost");

// Register typed RPC client interface
builder.Services.AddCentraServiceClient<IInventoryClient>();

// Register topic subscriber
builder.Services.AddCentraEventHandler<PaymentNotificationHandler, OrderCreatedEvent>();

var app = builder.Build();

// Map domain endpoints
app.MapPost("/orders", async (
    CreateOrderRequest request,
    IStateStore<Order> stateStore,
    IPubSubClient pubSub,
    IDistributedLockProvider lockProvider,
    IInventoryClient inventory) =>
{
    // 1. Distributed lock
    await using var @lock = await lockProvider.AcquireLockAsync(
        "lockstore", 
        request.ProductId, 
        expiryTime: TimeSpan.FromSeconds(30), 
        timeout: TimeSpan.FromSeconds(5));

    // 2. Strongly typed RPC invocation
    var inStock = await inventory.CheckStockAsync(request.ProductId, request.Quantity);
    if (!inStock) return Results.BadRequest("Out of stock");

    // 3. Persist state with optimistic concurrency (ETags)
    var order = new Order($"ord-{Guid.NewGuid():N}", request.CustomerId, request.ProductId, request.Quantity, "Created");
    await stateStore.SetAsync(order.Id, order);

    // 4. Publish CNCF CloudEvent v1.0
    await pubSub.PublishAsync("orders.created", new OrderCreatedEvent(order.Id, order.CustomerId, order.ProductId));

    return Results.Created($"/orders/{order.Id}", order);
});

// Mount CloudEvents and Bindings route dispatcher
app.MapCentraEndpoints();

app.Run();
```

### 2. Pure Domain Handlers & Typed Clients

```csharp
// Typed RPC Client Proxy: No boilerplate HTTP client code!
[ServiceClient("inventory-service")]
public interface IInventoryClient
{
    [ServiceMethod("items/check-stock", "POST")]
    Task<bool> CheckStockAsync(string productId, int quantity);
}

// Pure Event Handler: CloudEvent unpacking and W3C context linking handled automatically
[Topic("pubsub", "orders.created")]
public sealed class PaymentNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IStateStore<Order> _stateStore;

    public PaymentNotificationHandler(IStateStore<Order> stateStore)
    {
        _stateStore = stateStore;
    }

    public async Task<EventHandlingResult> HandleAsync(
        OrderCreatedEvent @event, 
        EventContext context, 
        CancellationToken cancellationToken)
    {
        var existing = await _stateStore.GetAsync(@event.OrderId, cancellationToken: cancellationToken);
        if (existing.HasValue)
        {
            var updated = existing.Value.Value with { Status = "ProcessingPayment" };
            await _stateStore.TrySetAsync(@event.OrderId, updated, existing.Value.ETag, cancellationToken: cancellationToken);
        }

        return EventHandlingResult.Success;
    }
}
```

### 3. Multi-Instance Cluster Example (`Centra.Sample.MultiInstance`)

Showcases distributed coordination and service discovery across multiple service instances:
- **Native Service Discovery & Client-Side Load Balancing**: Service instances resolve peer endpoints dynamically via `IServiceEndpointResolver` and `ControlPlaneServiceEndpointResolver` with round-robin dispatch, eliminating custom cluster coordinators.
- **Leader Election & Mutex Locking**: Multiple instances compete for a shared resource lock (`IDistributedLockProvider`); only one active leader with automatic failover.
- **Shared State & Optimistic Concurrency Control**: Multiple instances concurrently mutate shared state (`IStateStore<T>`) with automatic ETag collision detection and retries.
- **CNCF CloudEvents Pub/Sub**: Instances publish and consume domain events across the cluster with ambient W3C trace context.
- **Cluster Topology Tracking**: Each replica registers its `InstanceId`, service address metadata, and heartbeats with the Control Plane (`/api/v1/topology`).

**Run Options**:
```bash
# Option 1: Run the automated in-process 3-node simulation:
dotnet run --project samples/Centra.Sample.MultiInstance -- --demo

# Option 2: Run as a standalone web node:
dotnet run --project samples/Centra.Sample.MultiInstance -- --instance-id node-1 --urls "http://localhost:5101"

# Option 3: Run multi-replica cluster orchestrated with .NET Aspire:
dotnet run --project samples/Centra.AppHost
```

### 4. Distributed Virtual Actors Example (`Centra.Sample.Actors`)

Showcases stateful virtual actors with turn-based concurrency, durable reminders, and dynamic RPC dispatch:
- **Turn-Based Concurrency**: 50 concurrent client turns dispatch against an actor instance, executed strictly sequentially without race conditions or locks.
- **State Persistence & Passivation**: Inactive actors passivate to conserve resources; incoming turns or reminders seamlessly reactivate them with restored state.
- **Durable Reminders**: Reminders survive actor deactivation and cluster node failovers, executed with distributed lock mutual exclusion.
- **Dynamic Proxy Dispatch**: Interacting with actors is pure C# interface code via `IActorProxyFactory`.

**Run Simulation**:
```bash
dotnet run --project samples/Centra.Sample.Actors -- --demo
```

### 5. Distributed Workflows & Sagas Example (`Centra.Sample.Workflows`)

Showcases durable orchestrations, automated saga compensation rollbacks, durable timers, and CloudEvents external event awaits:
- **Deterministic Event-Sourced Replay**: History event stream re-executed on turn resumption; completed activities skip live invocation and return cached outputs.
- **Distributed Sagas & LIFO Compensation**: Activities register compensating reverse actions (`AddCompensation`); failures trigger automatic reverse rollback.
- **Durable Timers**: Asynchronous timer suspensions persisting due time and waking up automatically across cluster replicas without thread-blocking.
- **CloudEvents External Event Await**: Suspend workflows waiting for human-in-the-loop approval or domain events, resuming cleanly upon CloudEvent receipt.

**Run Simulation**:
```bash
dotnet run --project samples/Centra.Sample.Workflows -- --demo
```

---

## 🔌 Production Distributed Providers

Centra provides high-performance, production-ready distributed providers with zero sidecars and native C# drivers:

| Provider | Components Supported | Features | Package |
| :--- | :--- | :--- | :--- |
| **Redis** | State Store, Pub/Sub, Distributed Lock | Atomic Lua Compare-And-Swap (ETag validation), multi-key transactions, CloudEvents binary framing, consumer groups, DLQ, lock heartbeat renewal | `Centra.Providers.Redis` |
| **PostgreSQL** | State Store, Distributed Lock | Schema-isolated JSONB state table, ACID transactions, optimistic concurrency with ETags, TTL expiration pruning, mutual exclusion lease table | `Centra.Providers.PostgreSql` |
| **RabbitMQ** | Pub/Sub | AMQP topic exchange, CNCF CloudEvents headers mapping, dead-letter exchanges (DLX), durable queues, competing consumers | `Centra.Providers.RabbitMQ` |
| **SQL Server** | State Store, Distributed Lock | Atomic `MERGE` upsert, optimistic concurrency with ETags, `SqlTransaction` multi-operation batches, TTL expiration indexing, lease table mutual exclusion | `Centra.Providers.SqlServer` |
| **Azure Service Bus** | Pub/Sub | Cloud-native topics and subscriptions, CNCF CloudEvents application properties, W3C tracecontext propagation, dead-lettering, settlement (`Complete`, `Abandon`, `DeadLetter`) | `Centra.Providers.AzureServiceBus` |
| **Azure Cosmos DB** | State Store, Distributed Lock | Direct mode point reads, optimistic concurrency (`IfMatchEtag`), container TTL, `TransactionalBatch` single-partition ACID operations, lease locking with renewal | `Centra.Providers.CosmosDb` |

### Provider Configuration Examples

```csharp
// Redis: State Store, Pub/Sub, Distributed Locks
builder.Services.AddCentraRedis(options =>
{
    options.Configuration = "localhost:6379";
    options.InstanceName = "app:";
    options.DefaultDatabase = 0;
});

// PostgreSQL: State Store & Distributed Locks
builder.Services.AddCentraPostgreSql(options =>
{
    options.ConnectionString = "Host=localhost;Database=centra;Username=postgres;Password=postgres";
    options.Schema = "centra";
    options.AutoCreateSchema = true;
});

// RabbitMQ: CloudEvents AMQP Pub/Sub
builder.Services.AddCentraRabbitMQ(options =>
{
    options.HostName = "localhost";
    options.ExchangeName = "centra.events";
    options.ExchangeType = "topic";
    options.Durable = true;
});

// SQL Server: Enterprise State Store & Distributed Locks
builder.Services.AddCentraSqlServer(options =>
{
    options.ConnectionString = "Server=localhost,1433;Database=centra;User Id=sa;Password=Your_password123;TrustServerCertificate=True;";
    options.SchemaName = "dbo";
    options.AutoCreateTable = true;
});

// Azure Service Bus: Cloud-Native Pub/Sub
builder.Services.AddCentraAzureServiceBus(options =>
{
    options.ConnectionString = "Endpoint=sb://your-namespace.servicebus.windows.net/;SharedAccessKeyName=...;SharedAccessKey=...";
    options.SubscriptionName = "orders-worker";
});

// Azure Cosmos DB: Globally-Distributed State & Locks
builder.Services.AddCentraCosmosDb(options =>
{
    options.ConnectionString = "AccountEndpoint=https://your-account.documents.azure.com:443/;AccountKey=...;";
    options.DatabaseName = "centra";
    options.AutoCreateDatabaseAndContainers = true;
});
```

---

## 🧪 Testing & Interactive Simulations

The solution includes comprehensive unit, integration, and chaos simulation suites:

```bash
# Build the entire solution (TreatWarningsAsErrors is active)
dotnet build Centra.slnx

# Run all unit test suites (264 unit + 26 control plane + 37 provider tests)
dotnet test Centra.slnx --filter "Category!=Integration" --logger "console;verbosity=normal"

# Run interactive 3-node cluster simulation (leader election, shared state CAS, CloudEvents)
dotnet run --project samples/Centra.Sample.MultiInstance -- --demo

# Run interactive resilience & fault tolerance chaos simulation (retries, circuit breaker trip/recover, timeouts, dynamic hot-reloading)
dotnet run --project samples/Centra.Sample.Resilience -- --demo

# Run interactive distributed bindings & schedulers demo (cron triggers, inbound webhooks, resilient output bindings)
dotnet run --project samples/Centra.Sample.Bindings -- --demo

# Run interactive distributed virtual actors simulation (turn-based concurrency, state persistence, durable reminders)
dotnet run --project samples/Centra.Sample.Actors -- --demo

# Run interactive distributed workflows & sagas simulation (deterministic replay, LIFO compensations, timers, CloudEvents await)
dotnet run --project samples/Centra.Sample.Workflows -- --demo
```

---

## ⏰ Distributed Schedulers & Bindings Engine

Centra provides enterprise distributed cron scheduling and bi-directional I/O bindings with zero sidecars and zero-allocation performance:

### 1. High-Precision Zero-Allocation Cron Parser
- Bitmask-based jumping algorithm using 64-bit integer masks (`ulong`).
- Supports standard 5-part (`* * * * *`) and 6-part second-precision (`*/5 * * * * *`) cron expressions.
- Supports shorthand macros (`@every 5s`, `@hourly`, `@daily`, `@weekly`, `@monthly`, `@yearly`).
- Fully testable with .NET 10 `TimeProvider` abstractions.

### 2. Cluster-Aware Distributed Cron Coordination
- Guarantees exactly-once job execution across multi-replica service instances using distributed locks:
  `cron:{jobName}:{scheduledUnixTimestamp}`
- Active leader executes the tick; secondary nodes cleanly yield without throwing exceptions.

### 3. Bi-Directional Input & Output Bindings
- **Input Triggers**: Automatically exposes `/centra/bindings/{bindingName}` endpoints in ASP.NET Core, mapping inbound HTTP/webhooks directly to `IBindingTriggerHandler` with automatic W3C tracecontext extraction and latency metrics.
- **Output Bindings**: Invokes external services (`IOutputBinding`, `CentraOutputBinding`) through registered SPI drivers (`HttpWebhookBindingDriver`, `InMemoryBindingDriver`), seamlessly wrapped in Polly v8 resilience pipelines (`binding:{bindingName}`) and OpenTelemetry activities.

```csharp
// Register Cron Job & Inbound Webhook Trigger in Program.cs
builder.Services.AddCentraBindings();
builder.Services.AddCentraCronJob<InventorySnapshotJob>("inventory-sync", "0 * * * *");
builder.Services.AddCentraInputBindingHandler<OrdersWebhookTriggerHandler>("orders-webhook");
builder.Services.AddCentraHttpWebhookBinding("notifications-out");

// Pure Domain Handlers: Code Focused on Code
public sealed class InventorySnapshotJob : IJobHandler
{
    public async ValueTask ExecuteAsync(ScheduledJobContext context)
    {
        // Executes across the cluster exactly once per scheduled tick
    }
}
```

---

## 🛡️ Distributed Resilience & Fault Tolerance

Centra integrates **Polly Core v8** directly into its execution pipelines without sidecars or separate process boundaries. The resilience engine composes policies in a zero-allocation pipeline:

```
[Incoming Request] ──> [Timeout] ──> [Bulkhead / Concurrency] ──> [Rate Limiter] ──> [Circuit Breaker] ──> [Retry] ──> [Target Execution]
```

### Composable Resilience Registration

```csharp
builder.Services.AddCentraResilience(options =>
{
    // Configure targeted policy for downstream payment gateway
    options.AddPolicy(new CentraResiliencePolicyDefinition(
        PolicyName: "invocation:payment-gateway",
        Retry: new RetryPolicyOptions(
            MaxRetries: 3,
            BackoffType: CentraBackoffType.Exponential,
            BaseDelay: TimeSpan.FromMilliseconds(50),
            MaxDelay: TimeSpan.FromSeconds(2),
            UseJitter: true),
        CircuitBreaker: new CircuitBreakerPolicyOptions(
            FailureRatio: 0.5,
            SamplingDuration: TimeSpan.FromSeconds(10),
            MinimumThroughput: 5,
            BreakDuration: TimeSpan.FromSeconds(5)),
        Timeout: new TimeoutPolicyOptions(TimeSpan.FromSeconds(3))));

    // Configure global default fallback for state stores
    options.AddPolicy(new CentraResiliencePolicyDefinition(
        PolicyName: "default-state",
        Retry: new RetryPolicyOptions(
            MaxRetries: 3,
            BackoffType: CentraBackoffType.Exponential,
            BaseDelay: TimeSpan.FromMilliseconds(20))));
});
```

All RPC invocations (`CentraServiceInvoker`), state store mutations (`CentraStateStore`), message publications (`CentraPubSubClient`), and output bindings (`CentraOutputBinding`) automatically resolve their corresponding pipeline from `IResiliencePipelineProvider` unless explicitly bypassed.

---

## 🎭 Distributed Virtual Actors Runtime

Centra features a native, high-performance **Distributed Virtual Actors Runtime Engine** built for .NET 10. Virtual actors exist conceptually forever; they are activated on-demand upon first invocation and passivated when idle, with state backed by Centra's pluggable state stores.

```
┌──────────────────────────────────────────────────────────┐
│                   IActorProxyFactory                     │
└────────────────────────────┬─────────────────────────────┘
                             │ Creates Dynamic DispatchProxy
                             ▼
┌──────────────────────────────────────────────────────────┐
│               IActorPlacementDirector                    │
│    (ConsistentHashRing: Virtual Nodes Partitioning)      │
└──────────────┬────────────────────────────┬──────────────┘
               │ Local                      │ Remote HTTP
               ▼                            ▼
┌──────────────────────────────┐ ┌─────────────────────────┐
│         ActorManager         │ │ Remote Node (Actor HTTP)│
│  ┌────────────────────────┐  │ │ POST /centra/actors/    │
│  │     ActorMailbox       │  │ │      {type}/{id}/method │
│  │ (Turn-Based FIFO Queue)│  │ └─────────────────────────┘
│  └───────────┬────────────┘  │
│              ▼               │
│  ┌────────────────────────┐  │
│  │     Actor Instance     │  │
│  │  - ActorStateManager   │  │
│  │  - ActorTimerManager   │  │
│  │  - IActorReminderMgr   │  │
│  └───────────┬────────────┘  │
└──────────────┼───────────────┘
               │ Auto-Commit on Turn Completion
               ▼
┌──────────────────────────────────────────────────────────┐
│             IStateStore (ETag CAS Persistence)           │
└──────────────────────────────────────────────────────────┘
```

### Key Actor Capabilities

1. **Turn-Based Concurrency**: Every actor activation owns an `ActorMailbox`. Turns are queued and dispatched sequentially. An actor never processes two turns simultaneously, eliminating multi-threading race conditions without manual locks.
2. **Optimistic Concurrency State Manager**: State mutations are tracked in-memory through `ActorStateManager`. Upon turn completion, dirty keys are atomically committed to `IStateStore` using ETag Compare-And-Swap.
3. **Consistent Hash Partition Placement**: The `ConsistentHashRing` uniformly maps `ActorIdentity` across cluster replicas via 100 virtual vnodes per physical node, minimizing re-partitioning churn when nodes join or leave.
4. **Ephemeral Timers & Durable Reminders**:
   - **Timers**: In-memory periodic callbacks tied to an active actor's lifecycle (`ActorTimerManager`).
   - **Reminders**: Persistent schedules recorded in `IStateStore`. If an actor is passivated, `ActorReminderCoordinator` automatically wakes the actor up to execute `ReceiveReminderAsync`. Reminders coordinate using distributed locks to guarantee single-execution across the cluster.
5. **Dynamic Proxy Generation**: Strongly typed interfaces (e.g., `IAccountActor`) are dynamically proxied via `IActorProxyFactory.CreateActorProxy<T>()`. Method calls route locally if placed on the current node or fall back to HTTP RPC dispatch across cluster nodes.

```csharp
// Define Actor Contract & Remindable Hook
public interface IAccountActor : IActor, IRemindable
{
    ValueTask<decimal> GetBalanceAsync();
    ValueTask<decimal> DepositAsync(decimal amount);
    ValueTask ScheduleInterestReminderAsync(TimeSpan period);
}

// Implement Actor with State & Reminders
public sealed class AccountActor : Actor, IAccountActor
{
    public async ValueTask<decimal> DepositAsync(decimal amount)
    {
        var current = await StateManager.GetStateAsync<decimal>("balance");
        var updated = current + amount;
        await StateManager.SetStateAsync("balance", updated);
        return updated; // Auto-saved to state store at end of turn!
    }

    public async ValueTask ReceiveReminderAsync(string name, ReadOnlyMemory<byte> state, TimeSpan dueTime, TimeSpan period, CancellationToken ct)
    {
        // Executes across cluster with distributed lock coordination!
    }
}
```

---

## 🔄 Distributed Workflows & Sagas Runtime Engine

Centra features a native, high-performance **Distributed Workflows & Sagas Runtime Engine** engineered for .NET 10. Workflows orchestrate multi-step, resilient business logic with deterministic event-sourced replay, durable timers, CloudEvents human-in-the-loop external event awaits, and first-class sagas with automated LIFO compensation rollbacks.

```
┌────────────────────────────────────────────────────────┐
│                   IWorkflowClient                      │
└───────────────────────────┬────────────────────────────┘
                            │ Starts / Awaits / Raises Event
                            ▼
┌────────────────────────────────────────────────────────┐
│                   IWorkflowEngine                      │
│ ┌────────────────────────────────────────────────────┐ │
│ │            Deterministic Replay Engine             │ │
│ │   - Reads past history from IWorkflowHistoryStore  │ │
│ │   - Skips completed activities (returns cached)    │ │
│ │   - Deterministic Clock (CurrentUtcDateTime)       │ │
│ │   - Deterministic Guid Generator (NewGuid())       │ │
│ └─────────────────────────┬──────────────────────────┘ │
│                           │ Live Dispatch              │
│                           ▼                            │
│ ┌────────────────────────────────────────────────────┐ │
│ │           IWorkflowActivityDispatcher              │ │
│ │   - Wrapped in Polly v8 Resilience Pipeline        │ │
│ │   - System.Diagnostics Tracing (ActivitySource)    │ │
│ └─────────────────────────┬──────────────────────────┘ │
│                           │ On Step Failure            │
│                           ▼                            │
│ ┌────────────────────────────────────────────────────┐ │
│ │             IWorkflowSaga (LIFO)                   │ │
│ │   - Executes registered compensations in reverse   │ │
│ └────────────────────────────────────────────────────┘ │
└───────────────────────────┬────────────────────────────┘
                            │ Atomic Event Append & State CAS
                            ▼
┌────────────────────────────────────────────────────────┐
│        IWorkflowHistoryStore (StateStore CAS)          │
│   - WorkflowStateRecord (Running, Suspended, etc.)     │
│   - WorkflowHistoryRecord (Append-Only Event Stream)   │
└────────────────────────────────────────────────────────┘
```

### Key Workflow Capabilities

1. **Deterministic Event-Sourced Replay**: The orchestrator function is replayed from scratch on every turn. The `DeterministicWorkflowContext` matches past history against activity invocations; completed activities immediately return cached data without re-executing external side effects.
2. **First-Class Sagas with Automated LIFO Compensations**: Activities can register compensating actions via `context.CreateSaga().AddCompensation<TComp, TInput>(input)`. If an activity subsequently fails, registered compensations execute automatically in strict reverse (LIFO) order.
3. **Durable Timers**: `await context.CreateTimerAsync(TimeSpan)` suspends workflow execution, persists the due time to the state store, and schedules a wake-up callback via `TimeProvider` without holding open worker threads.
4. **CloudEvents External Event Awaits**: `await context.WaitForExternalEventAsync<TEvent>("EventName")` suspends workflow execution until an external CloudEvent arrives via `IWorkflowClient.RaiseEventAsync`.
5. **Observability & Resilience**: Every activity dispatch is automatically wrapped in an OpenTelemetry activity and Polly Core v8 resilience pipeline (`workflow:activity:{name}`) with exponential backoff and jitter.

```csharp
// Define Activity
public sealed class ReserveInventoryActivity : WorkflowActivity<OrderProcessingRequest, InventoryReservation>
{
    public override ValueTask<InventoryReservation> RunAsync(WorkflowActivityContext context, OrderProcessingRequest input)
    {
        return ValueTask.FromResult(new InventoryReservation($"res-{Guid.NewGuid():N}"[..8], input.ProductId, input.Quantity));
    }
}

// Define Saga Compensating Activity
public sealed class ReleaseInventoryCompensationActivity : WorkflowActivity<InventoryReservation, bool>
{
    public override ValueTask<bool> RunAsync(WorkflowActivityContext context, InventoryReservation input)
    {
        // Compensates by releasing held inventory
        return ValueTask.FromResult(true);
    }
}

// Orchestrate Workflow with Saga Compensation
public sealed class OrderProcessingWorkflow : Workflow<OrderProcessingRequest, OrderProcessingResult>
{
    public override async ValueTask<OrderProcessingResult> RunAsync(IWorkflowContext context, OrderProcessingRequest input)
    {
        var saga = context.CreateSaga();

        // Step 1: Reserve inventory and register compensation
        var reservation = await context.CallActivityAsync<ReserveInventoryActivity, OrderProcessingRequest, InventoryReservation>(input);
        saga.AddCompensation<ReleaseInventoryCompensationActivity, InventoryReservation>(reservation);

        try
        {
            // Step 2: Charge payment (if this fails, compensation triggers automatically)
            var txnId = await context.CallActivityAsync<ProcessPaymentActivity, OrderProcessingRequest, string>(input);

            // Step 3: Durable timer delay
            await context.CreateTimerAsync(TimeSpan.FromSeconds(30));

            // Step 4: Ship
            var tracking = await context.CallActivityAsync<ShipOrderActivity, OrderProcessingRequest, string>(input);
            return new OrderProcessingResult(input.OrderId, "Completed", txnId, tracking, "Fulfilled successfully.");
        }
        catch (WorkflowSuspendedException)
        {
            throw; // Allow timer/event suspension to propagate
        }
        catch (Exception ex)
        {
            await saga.CompensateAsync(); // Roll back inventory in reverse order
            return new OrderProcessingResult(input.OrderId, "Failed", null, null, $"Compensated: {ex.Message}");
        }
    }
}
```

---

## 📦 Solution Architecture

```
Centra.slnx
├── src/
│   ├── Centra.Abstractions/           # Umbrella metapackage referencing all modular abstractions
│   ├── Centra.Events.Abstractions/    # CNCF CloudEvents v1.0 contracts & ambient context
│   ├── Centra.PubSub.Abstractions/    # IPubSubClient, IEventHandler, Topic contracts
│   ├── Centra.State.Abstractions/     # IStateStore, StateEntry, transactions, optimistic concurrency
│   ├── Centra.Locks.Abstractions/     # IDistributedLockProvider & IDistributedLock contracts
│   ├── Centra.Invocation.Abstractions/# IServiceInvoker, IServiceEndpointResolver & typed RPC client attributes
│   ├── Centra.Bindings.Abstractions/  # IInputBinding, IOutputBinding, IScheduler, IJobHandler & Cron contracts
│   ├── Centra.Components.Abstractions/# ComponentDefinition, ComponentType, IComponentRegistry
│   ├── Centra.Sync.Abstractions/      # IControlPlaneClient & live streaming sync event DTOs
│   ├── Centra.Resilience.Abstractions/# Resilience pipelines, retry, circuit breaker, timeout & bulkhead contracts
│   ├── Centra.Actors.Abstractions/    # IActor, Actor, ActorId, IActorStateManager, IActorReminderManager contracts
│   ├── Centra.Workflows.Abstractions/ # IWorkflow, IWorkflowActivity, IWorkflowContext, IWorkflowSaga, IWorkflowClient
│   ├── Centra.Core/                   # In-process runtime, zero-alloc serialization, cron, RPC proxies, Polly v8, actors & workflows
│   ├── Centra.Providers.InMemory/     # Zero-dependency in-memory driver implementations
│   ├── Centra.Providers.Redis/        # Redis State (Lua CAS/Tx), Pub/Sub (CloudEvents v1.0 binary), Locks (Lease/Renewal)
│   ├── Centra.Providers.PostgreSql/   # PostgreSQL State (ACID table, ETags, Tx, TTL) & Locks (Lease table heartbeat)
│   ├── Centra.Providers.RabbitMQ/     # RabbitMQ Pub/Sub (AMQP topic exchange, CloudEvents headers, consumer groups, DLX)
│   ├── Centra.Providers.SqlServer/    # SQL Server State (MERGE, ETags, Tx, TTL) & Locks (Lease table renewal)
│   ├── Centra.Providers.AzureServiceBus/# Azure Service Bus Pub/Sub (Topics, Subscriptions, CloudEvents headers, Dead-lettering)
│   ├── Centra.Providers.CosmosDb/     # Azure Cosmos DB State (Point reads, TransactionalBatch, ETags, TTL) & Locks
│   ├── Centra.Hosting/                # ASP.NET Core minimal APIs, actor endpoints, workflow endpoints, hosted services
│   ├── Centra.ControlPlane/           # Central component catalog, resilience, topology, actor & workflow inspection, SSE
│   └── Centra.Aspire.Hosting/         # .NET Aspire AppHost integration, resource mapping extensions
├── samples/
│   ├── Centra.Sample.OrdersService/   # Real-world ASP.NET Core sample microservice
│   ├── Centra.Sample.MultiInstance/   # Multi-instance cluster: native service discovery, locks, shared state, pub/sub
│   ├── Centra.Sample.Resilience/      # Resilience & Chaos simulation: retries, circuit breaker trip/recover, timeouts, hot-reload
│   ├── Centra.Sample.Bindings/        # Bindings simulation: distributed cron, HTTP webhooks, and state persistence
│   ├── Centra.Sample.Actors/          # Virtual actors simulation: turn-based concurrency, state persistence, durable reminders
│   ├── Centra.Sample.Workflows/       # Workflows simulation: deterministic replay, distributed sagas, timers, CloudEvents await
│   └── Centra.AppHost/                # .NET Aspire cloud-native AppHost orchestrator (multi-replica orchestration)
└── tests/
    ├── Centra.Tests.Unit/             # Core, runtime, state, pubsub, bindings, resilience, actors & workflows tests (264 tests)
    ├── Centra.ControlPlane.Tests.Unit/# Control Plane catalog, resilience, topology, actors & workflows tests (26 tests)
    ├── Centra.Providers.Redis.Tests.Unit/        # Redis State, Pub/Sub, and Locks unit tests (17 tests)
    ├── Centra.Providers.CosmosDb.Tests.Unit/    # Azure Cosmos DB State and Locks unit tests (7 tests)
    ├── Centra.Providers.SqlServer.Tests.Unit/   # SQL Server State and Locks unit tests (5 tests)
    ├── Centra.Providers.AzureServiceBus.Tests.Unit/ # Azure Service Bus Pub/Sub unit tests (4 tests)
    ├── Centra.Providers.RabbitMQ.Tests.Unit/    # RabbitMQ Pub/Sub unit tests (2 tests)
    ├── Centra.Providers.PostgreSql.Tests.Unit/  # PostgreSQL State and Locks unit tests (2 tests)
    └── Centra.Tests.Integration/      # End-to-end workflows, multi-instance cluster, bindings, actors, sagas & Testcontainers (24 tests)
```

---

## 📜 Engineering Invariants
- **SOLID**: Interface segregation (ISP), single responsibility (SRP), dependency inversion (DIP).
- **TDD**: Red/Green/Refactor test-first development.
- **File per Type**: Exactly one type per `.cs` file.
- **Zero Allocation**: `ValueTask`, `readonly record struct`, `ReadOnlyMemory<byte>`, `ArrayPool<byte>`.
- **Standards Compliant**: CNCF CloudEvents v1.0, W3C TraceContext, OpenTelemetry semantic conventions.
- **Actor Concurrency Safety**: Single-threaded FIFO turn execution via `ActorMailbox` with optimistic concurrency ETag commits.
- **Durable Reminders Mutual Exclusion**: Distributed lock coordination across cluster nodes preventing duplicate ticks.
- **Workflow Determinism & Saga Rollback Invariant**: Orchestration turns must be deterministic; side effects, clock checks, and random values must execute within activities or use `IWorkflowContext` (`CurrentUtcDateTime`, `NewGuid()`). On activity failure or cancellation, registered saga compensations must execute in strict reverse (LIFO) order.

