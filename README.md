# Centra: Distributed Application Framework for .NET 10

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512bd4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-817%20Passed-brightgreen.svg)]()
[![CloudEvents](https://img.shields.io/badge/CNCF-CloudEvents%20v1.0-orange.svg)](https://cloudevents.io/)
[![Zero Sidecars](https://img.shields.io/badge/Architecture-Zero%20Sidecars-success.svg)](docs/architecture/overview.md)

> A high-performance, cloud-native distributed application framework for .NET 10 engineered natively in C# to deliver zero-sidecar in-process speed, modular abstractions adhering strictly to ISP, centralized component governance, CNCF CloudEvents v1.0 messaging, Polly Core v8 resilience, distributed virtual actors, deterministic workflows & sagas, and pure domain code where **"code is focused on code"**.

---

## ⚡ Why Centra?

Traditional distributed runtimes (such as Dapr) mandate a separate sidecar process (e.g., in Go or Rust) communicating over a loopback HTTP or gRPC socket. In high-throughput .NET workloads, this introduces:
- **Loopback Latency Tax**: 1.5–4.0 ms per hop across the OS TCP stack.
- **Double-Serialization Overhead**: App -> JSON/gRPC -> Sidecar -> Physical Driver.
- **Memory Footprint Penalty**: 30–80 MB extra container RAM per replica.
- **Fragmented Configuration**: Drifting YAML manifests across environments.

**Centra solves this natively in C#:**
- **Zero Sidecars**: Compiles directly into your application process—native method invocations into high-performance C# SPI drivers.
- **Zero Allocation Discipline**: Built on `ValueTask`, `readonly record struct`, `ReadOnlyMemory<byte>`, and `ArrayPool<byte>.Shared`.
- **Pure Domain Ergonomics**: Interact with clean interfaces (`IStateStore<T>`, `IPubSubClient`, `IDistributedLockProvider`, `IActorProxyFactory`, `IWorkflowClient`) with zero infrastructure boilerplate.
- **Centralized Governance**: Real-time component hot-reloading over Server-Sent Events (SSE) via the Centra Control Plane.

> 📖 *Learn more in the [Architectural Overview & Design Principles](docs/architecture/overview.md).*

---

## 🏛️ Key Architectural Pillars

| Pillar | Summary | Deep Dive |
| :--- | :--- | :--- |
| **Native In-Process Speed** | Zero-sidecar runtime eliminating loopback latency and serialization hops. | [Overview](docs/architecture/overview.md) |
| **Modular Abstractions (ISP)** | 11 fine-grained abstraction libraries; consume only what you use with zero dependency drag. | [Package Ecosystem](docs/architecture/package-ecosystem.md) |
| **Distributed State** | Optimistic concurrency (ETags), atomic multi-key transactions, and TTL. | [State Management](docs/building-blocks/state-management.md) |
| **CNCF CloudEvents v1.0** | At-least-once messaging, Binary/Structured framing, CEL rule filtering, and DLQ. | [Pub/Sub Messaging](docs/building-blocks/pubsub.md) |
| **Distributed Locks** | Mutual exclusion leases with automatic background heartbeat renewal and leader election. | [Distributed Locks](docs/building-blocks/distributed-locks.md) |
| **Service Invocation & RPC** | Dynamic and compile-time client proxies with client-side round-robin load balancing. | [Service Invocation](docs/building-blocks/service-invocation.md) |
| **Virtual Actors** | Turn-based single-threaded mailboxes, consistent hash ring placement, and durable reminders. | [Virtual Actors](docs/building-blocks/virtual-actors.md) |
| **Workflows & Sagas** | Deterministic event-sourced replay, durable timers, and automated LIFO sagas. | [Workflows & Sagas](docs/building-blocks/workflows-and-sagas.md) |
| **Distributed Resilience** | Polly Core v8 composite pipelines (Timeout -> Bulkhead -> RateLimiter -> CircuitBreaker -> Retry). | [Resilience](docs/building-blocks/resilience.md) |
| **Distributed Schedulers** | Bitmask 64-bit cron parser with cluster-wide single-execution locks and webhook triggers. | [Schedulers & Bindings](docs/building-blocks/schedulers-and-bindings.md) |
| **Roslyn Source Generators** | Incremental source generators emitting zero-reflection RPC and actor client proxies at compile time. | [Package Ecosystem](docs/architecture/package-ecosystem.md#roslyn-incremental-source-generators) |
| **Centralized Governance** | Central catalog, secret resolution, topology heartbeats, and real-time SSE hot-reload. | [Control Plane](docs/operations/control-plane.md) |

---

## 🚀 Quickstart: First Distributed Microservice

Build and run a complete microservice with distributed locks, typed RPC, state persistence, and CloudEvents pub/sub in under 60 seconds:

```csharp
// Program.cs
using Centra.Hosting.Extensions;
using Centra.Locks;
using Centra.PubSub;
using Centra.State;

var builder = WebApplication.CreateBuilder(args);

// 1. Register Centra (scans assembly for [Topic], [ServiceClient], [Actor], [Workflow])
builder.Services.AddCentra(options =>
{
    options.AppId = "orders-service";
    options.DefaultStateStore = "statestore";
    options.DefaultPubSub = "pubsub";
    options.DefaultLockStore = "lockstore";
});

// 2. Add Providers (In-Memory for testing; or Redis, RabbitMQ, PostgreSQL, etc.)
builder.Services.AddCentraInMemory();

var app = builder.Build();

// 3. Pure Domain Minimal API
app.MapPost("/orders", async (
    CreateOrderRequest request,
    IStateStore<Order> stateStore,
    IPubSubClient pubSub,
    IDistributedLockProvider lockProvider,
    IInventoryClient inventory) =>
{
    // A. Distributed mutex lock
    await using var @lock = await lockProvider.AcquireLockAsync(
        "lockstore", request.ProductId, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

    if (!@lock.Success) return Results.Conflict("Product is locked by another operation.");

    // B. Strongly typed RPC invocation (zero HttpClient plumbing)
    if (!await inventory.CheckStockAsync(request.ProductId, request.Quantity))
        return Results.BadRequest("Insufficient inventory.");

    // C. Persist state with optimistic concurrency (ETags)
    var order = new Order($"ord-{Guid.NewGuid():N}"[..12], request.CustomerId, request.ProductId, request.Quantity, "Created");
    await stateStore.SetAsync(order.Id, order);

    // D. Publish CNCF CloudEvent v1.0 with ambient W3C trace context
    await pubSub.PublishAsync("orders.created", new OrderCreatedEvent(order.Id, order.CustomerId, order.ProductId));

    return Results.Created($"/orders/{order.Id}", order);
});

// 4. Mount CloudEvents and Bindings route dispatcher
app.MapCentraEndpoints();

app.Run();
```

```csharp
// Domain Contracts & Handlers
public readonly record struct CreateOrderRequest(string CustomerId, string ProductId, int Quantity);
public sealed record Order(string Id, string CustomerId, string ProductId, int Quantity, string Status);
public readonly record struct OrderCreatedEvent(string OrderId, string CustomerId, string ProductId);

// Strongly typed RPC client proxy
[ServiceClient("inventory-service")]
public interface IInventoryClient
{
    [ServiceMethod("items/check-stock", "POST")]
    Task<bool> CheckStockAsync(string productId, int quantity);
}

// Pure Event Handler with automatic CloudEvents unpacking & W3C context linking
[Topic("pubsub", "orders.created")]
public sealed class PaymentNotificationHandler(IStateStore<Order> stateStore) : IEventHandler<OrderCreatedEvent>
{
    public async Task<EventHandlingResult> HandleAsync(OrderCreatedEvent @event, EventContext context, CancellationToken ct)
    {
        var existing = await stateStore.GetAsync(@event.OrderId, cancellationToken: ct);
        if (existing.HasValue)
        {
            var updated = existing.Value.Value with { Status = "ProcessingPayment" };
            await stateStore.TrySetAsync(@event.OrderId, updated, existing.Value.ETag, cancellationToken: ct);
        }
        return EventHandlingResult.Success;
    }
}
```

> 📖 *Follow the step-by-step tutorial in the [Quickstart Guide](docs/getting-started/quickstart.md).*

---

## 🧱 Building Blocks

Centra's modular architecture lets you adopt building blocks incrementally:

| Building Block | Key Interfaces & Attributes | Guide |
| :--- | :--- | :--- |
| **State Management** | `IStateStore<T>`, `IStateStore`, `ITransactionalStateStore` | [Read Guide](docs/building-blocks/state-management.md) |
| **Pub/Sub Messaging** | `IPubSubClient`, `IEventHandler<T>`, `[Topic]`, `RuleFilter` | [Read Guide](docs/building-blocks/pubsub.md) |
| **Distributed Locks** | `IDistributedLockProvider`, `IDistributedLock` | [Read Guide](docs/building-blocks/distributed-locks.md) |
| **Service Invocation** | `[ServiceClient]`, `[ServiceMethod]`, `IServiceInvoker` | [Read Guide](docs/building-blocks/service-invocation.md) |
| **Schedulers & Bindings** | `IScheduler`, `[CronBinding]`, `IBindingTriggerHandler`, `IOutputBinding` | [Read Guide](docs/building-blocks/schedulers-and-bindings.md) |
| **Virtual Actors** | `IActor`, `Actor`, `IActorProxyFactory`, `IActorStateManager`, `IRemindable` | [Read Guide](docs/building-blocks/virtual-actors.md) |
| **Workflows & Sagas** | `Workflow<TIn, TOut>`, `WorkflowActivity<TIn, TOut>`, `IWorkflowClient` | [Read Guide](docs/building-blocks/workflows-and-sagas.md) |
| **Distributed Resilience** | `IResiliencePipelineProvider`, `CentraResiliencePolicyDefinition` | [Read Guide](docs/building-blocks/resilience.md) |

---

## 🔌 Production Distributed Providers

Centra provides high-performance, native C# driver implementations with zero sidecars:

| Provider | Capabilities Supported | Package | Guide |
| :--- | :--- | :--- | :--- |
| **Redis** | State Store (Lua CAS), Pub/Sub (CloudEvents binary), Distributed Locks (Lease renewal) | `Centra.Providers.Redis` | [Read Guide](docs/providers/redis.md) |
| **PostgreSQL** | State Store (JSONB tables, ACID batches, ETags, TTL), Distributed Locks | `Centra.Providers.PostgreSql` | [Read Guide](docs/providers/postgresql.md) |
| **RabbitMQ** | Pub/Sub (AMQP topic exchange, CloudEvents headers, consumer groups, DLX) | `Centra.Providers.RabbitMQ` | [Read Guide](docs/providers/rabbitmq.md) |
| **SQL Server** | State Store (Atomic `MERGE`, ETags, `SqlTransaction` batches, TTL), Distributed Locks | `Centra.Providers.SqlServer` | [Read Guide](docs/providers/sql-server.md) |
| **Azure Service Bus** | Pub/Sub (Cloud-native topics, subscriptions, CloudEvents application properties, DLQ) | `Centra.Providers.AzureServiceBus` | [Read Guide](docs/providers/azure-service-bus.md) |
| **Azure Cosmos DB** | State Store (Point reads, `TransactionalBatch` single-partition ACID, ETags, TTL), Distributed Locks | `Centra.Providers.CosmosDb` | [Read Guide](docs/providers/cosmosdb.md) |
| **In-Memory** | State Store, Pub/Sub, Distributed Locks, Output Bindings (zero-dependency testing) | `Centra.Providers.InMemory` | [Read Guide](docs/providers/in-memory.md) |

---

## ⚙️ Operations, Orchestration & Observability

Centra supports two flexible operational topologies: **Standalone Mode** (direct drivers + native DNS/K8s/Aspire) and **Orchestrated Mode** (centralized governance via Control Plane).

- **[When to Use Control Plane (Decision Guide)](docs/operations/when-to-use-control-plane.md)**: Architectural decision matrix comparing Standalone vs. Orchestrated modes.
- **[Centra Control Plane Architecture & REST API](docs/operations/control-plane.md)**: Central catalog, secret resolution, topology heartbeats, and real-time SSE synchronization.
- **[.NET Aspire Cloud-Native Orchestration](docs/getting-started/aspire.md)**: Local multi-replica orchestration with automatic dashboard, telemetry, and service discovery.
- **[Docker Compose 3-Node Cluster](docs/getting-started/docker-cluster.md)**: Complete containerized stack with Redis, RabbitMQ, Control Plane, OpenTelemetry, and Grafana.
- **[Observability & Telemetry Reference](docs/operations/observability.md)**: OpenTelemetry `ActivitySource("Centra")`, `Meter("Centra")`, semantic span conventions, and metrics catalog.
- **[Configuration & Options Reference](docs/operations/configuration-reference.md)**: Declarative attributes vs. service collection extensions, options classes, and environment variable bindings.
- **[NuGet Package Publishing & OIDC Trusted Publishing](docs/operations/nuget-publishing.md)**: Automated CI/CD packaging and release workflows.

---

## 🧪 Interactive Simulations & Samples

The repository includes ready-to-run interactive simulations demonstrating every framework capability:

```bash
# 1. Multi-instance cluster simulation (Leader election, shared state CAS, CloudEvents)
dotnet run --project samples/Centra.Sample.MultiInstance -- --demo

# 2. Virtual actors simulation (Turn-based concurrency, state persistence, durable reminders)
dotnet run --project samples/Centra.Sample.Actors -- --demo

# 3. Workflows & sagas simulation (Deterministic replay, LIFO compensations, durable timers)
dotnet run --project samples/Centra.Sample.Workflows -- --demo

# 4. Resilience & chaos simulation (Retries, circuit breaker trip/recovery, dynamic hot-reload)
dotnet run --project samples/Centra.Sample.Resilience -- --demo

# 5. Distributed bindings & schedulers simulation (Distributed cron, webhooks, output bindings)
dotnet run --project samples/Centra.Sample.Bindings -- --demo

# 6. Standalone RabbitMQ & typed RPC simulation (.NET Aspire)
dotnet run --project samples/RabbitSimulation/AppHost

# 7. Aspire multi-replica cloud orchestrator
dotnet run --project samples/Centra.AppHost

# 8. Full Docker Compose 3-node cluster (Redis, RabbitMQ, Control Plane, OTel, Grafana)
docker compose -f samples/DockerStack/docker-compose.yml up --build
```

---

## 📦 Package Ecosystem & Solution Map

```
Centra.slnx
├── src/
│   ├── Centra.Abstractions/           # Umbrella metapackage referencing all modular abstractions
│   ├── Centra.*.Abstractions/         # 11 Modular abstractions (Events, State, PubSub, Locks, ...)
│   ├── Centra.*                       # 12 Modular implementations (Runtime, Serialization, Resilience, ...)
│   ├── Centra.Generators/             # Roslyn incremental source generator for compile-time proxies
│   ├── Centra.Providers.*             # 7 Distributed provider drivers (Redis, PG, RabbitMQ, ...)
│   ├── Centra.Hosting/                # ASP.NET Core hosting, endpoints, attribute scanner, DI extensions
│   ├── Centra.ControlPlane/           # Central catalog, secret resolver, topology tracker, SSE dispatcher
│   └── Centra.Aspire.Hosting/         # .NET Aspire AppHost integration extensions
├── samples/                           # 8 Runnable sample applications and interactive simulations
└── tests/                             # 817+ Unit, Control Plane, Provider, and Integration test suites
```

> 📖 *View the complete layering diagram and package breakdown in the [Package Ecosystem Documentation](docs/architecture/package-ecosystem.md).*

---

## 🧪 Testing & Quality Gates

Every commit is verified by automated test suites running against unit mocks and real containerized infrastructure via Testcontainers:

```bash
# Run all unit test suites (791 tests)
dotnet test Centra.slnx --filter "Category!=Integration"

# Run all unit and integration tests (817+ tests, requires Docker for Testcontainers)
dotnet test Centra.slnx --logger "console;verbosity=normal"
```

### Test Suite Breakdown

| Test Project | Category | Tests | Focus |
| :--- | :--- | :--- | :--- |
| `Centra.Tests.Unit` | Unit | 572 | Core runtime, state, pub/sub, bindings, actors, workflows, resilience |
| `Centra.ControlPlane.Tests.Unit` | Unit | 55 | Component catalog, secret resolution, topology tracking, SSE sync |
| `Centra.Providers.CosmosDb.Tests.Unit` | Unit | 64 | Azure Cosmos DB state, ETags, and distributed locks |
| `Centra.Providers.Redis.Tests.Unit` | Unit | 38 | Redis state (Lua CAS), pub/sub, and lock lease renewal |
| `Centra.Providers.RabbitMQ.Tests.Unit` | Unit | 26 | RabbitMQ topic exchange, CloudEvents headers, consumer options |
| `Centra.Providers.AzureServiceBus.Tests.Unit` | Unit | 19 | Azure Service Bus topics, subscriptions, and settlement |
| `Centra.Providers.PostgreSql.Tests.Unit` | Unit | 7 | PostgreSQL schema initialization, ETag CAS, and lock leases |
| `Centra.Providers.SqlServer.Tests.Unit` | Unit | 5 | SQL Server MERGE upserts, ACID transactions, and lock leases |
| `Centra.Generators.Tests.Unit` | Unit | 5 | Roslyn incremental source generator proxy emissions |
| `Centra.Tests.Integration` | Integration | 26 | End-to-end workflows, multi-node clusters, sagas, Testcontainers |
| **Total** | | **817+** | **100% Passing** |

---

## 📜 Engineering Invariants

- **SOLID Principles**: Strict adherence to the Interface Segregation Principle (ISP) and Dependency Inversion Principle (DIP).
- **Single Responsibility (File per Type)**: Exactly one type per `.cs` file.
- **Zero Allocation**: `ValueTask`, `readonly record struct`, `ReadOnlyMemory<byte>`, and `ArrayPool<byte>.Shared` across hot execution paths.
- **Standards Compliant**: CNCF CloudEvents v1.0 (binary mode default) and W3C TraceContext propagation (`traceparent`, `tracestate`).
- **No Blind Overwrites**: State mutations use ETags and optimistic concurrency (`TrySetAsync`).
- **Actor Concurrency Safety**: Single-threaded FIFO turn execution via `ActorMailbox` with optimistic concurrency ETag commits.
- **Durable Reminders Mutual Exclusion**: Distributed lock coordination across cluster nodes preventing duplicate ticks.
- **Workflow Determinism & Saga Rollback**: Replay turns are strictly deterministic; activity failures trigger automated reverse (LIFO) compensations.
- **No Private Instance or Static Methods**: Types remain cohesive with logic inlined or factored into dedicated collaborator types.

---

## 📚 Complete Documentation Hub

For the complete documentation index organized by the Divio documentation system, visit the **[Centra Documentation Index](docs/index.md)**.
