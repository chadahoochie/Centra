---
name: centra
description: Enforces architectural standards, modular abstractions, provider SPI drivers, CNCF CloudEvents v1.0 contracts, zero-allocation conventions, and testing practices for Centra (high-performance distributed application framework for .NET 10). Activate this skill when authoring, modifying, or reviewing code in or using Centra.
---

# Centra Framework Architecture & Coding Standards

This skill defines the mandatory architectural invariants, implementation patterns, performance guidelines, and testing conventions for applications developing or using the **Centra Distributed Application Framework**—a modern, cloud-native distributed application framework for .NET 10 engineered natively in C# to eliminate sidecar latency, unify component governance, standardize messaging on CNCF CloudEvents v1.0, and allow developers to write pure domain logic.

---

## 🏛️ 1. Architectural Overview & Six Core Pillars

1. **Native In-Process Performance (Zero Sidecars)**:
   - Eliminates sidecar loopback HTTP/gRPC serialization hops and separate process overhead.
   - High-throughput in-process pipeline built on `ValueTask`, `readonly record struct`, `ReadOnlyMemory<byte>`, and `ArrayPool<byte>.Shared`.
2. **Modular & Pluggable Abstractions (Zero Dependency Drag)**:
   - Fine-grained, decoupled contracts adhering strictly to the Interface Segregation Principle (ISP).
   - Consume only what is needed (e.g. `Centra.PubSub.Abstractions` without dragging state stores or locks).
   - Composable DI registrations (`AddCentraPubSub`, `AddCentraState`, `AddCentraLocks`, `AddCentraInvocation`, `AddCentraBindings`, or full-stack `AddCentra`).
3. **Centralized Component Management**:
   - Eliminates fragmented YAML component manifests and Kubernetes CRD drift.
   - Components (State Stores, Pub/Sub Brokers, Distributed Locks, Bindings) are versioned and monitored centrally via [`Centra.ControlPlane`](file:///home/chad/source/dotnet/distributed-framework/src/Centra.ControlPlane) with real-time SSE hot-reloading.
4. **Observability as a Core Tenant**:
   - **Distributed Tracing**: Built on `System.Diagnostics.ActivitySource("Centra", "1.0.0")` with W3C `DistributedContextPropagator` context propagation (`traceparent`, `tracestate`).
   - **Semantic Span Roles**: Correct `ActivityKind` assignment (`Producer` on publish, `Consumer` on subscribe, `Client` on RPC invocation, `Server` on RPC handler, `Internal` on state/locks).
   - **Metrics**: Built on `System.Diagnostics.Metrics.Meter("Centra", "1.0.0")` reporting operation counters, latency histograms, and active gauges.
   - **High-Performance Logging**: Zero-allocation `[LoggerMessage]` source generators.
5. **CNCF CloudEvents v1.0 Standard**:
   - Transparently wraps and unwraps domain models into CloudEvents v1.0.
   - Supports **Binary Mode** (default zero-allocation: raw body + `ce-*` headers) and **Structured Mode** (single JSON payload).
   - Automatically tracks enterprise extensions: `ce-correlationid`, `ce-causationid`, `ce-tenantid`, `ce-schemaversion`.
6. **Code Focused on Code**:
   - Domain developers interact with clean, strongly typed interfaces ([`IStateStore<T>`](file:///home/chad/source/dotnet/distributed-framework/src/Centra.State.Abstractions/IStateStoreT.cs), [`IPubSubClient`](file:///home/chad/source/dotnet/distributed-framework/src/Centra.PubSub.Abstractions/IPubSubClient.cs), [`IDistributedLockProvider`](file:///home/chad/source/dotnet/distributed-framework/src/Centra.Locks.Abstractions/IDistributedLockProvider.cs), typed RPC proxies) without vendor plumbing.

---

## 📦 2. Solution Structure & Package Ecosystem

```
Centra.slnx
├── src/
│   ├── Centra.Abstractions/           # Umbrella metapackage referencing all 8 modular abstractions
│   ├── Centra.Events.Abstractions/    # CNCF CloudEvents v1.0 contracts & ambient context
│   ├── Centra.PubSub.Abstractions/    # IPubSubClient, IEventHandler, Topic contracts
│   ├── Centra.State.Abstractions/     # IStateStore<T>, StateEntry<T>, transactions, optimistic concurrency
│   ├── Centra.Locks.Abstractions/     # IDistributedLockProvider & IDistributedLock contracts
│   ├── Centra.Invocation.Abstractions/# IServiceInvoker, IServiceEndpointResolver & typed RPC attributes
│   ├── Centra.Bindings.Abstractions/  # IOutputBinding & Cron trigger contracts
│   ├── Centra.Components.Abstractions/# ComponentDefinition, ComponentType, IComponentRegistry
│   ├── Centra.Sync.Abstractions/      # IControlPlaneClient & live streaming sync event DTOs
│   ├── Centra.Core/                   # In-process runtime, zero-alloc serialization, diagnostics, RPC proxies
│   ├── Centra.Providers.InMemory/     # Zero-dependency in-memory driver implementations
│   ├── Centra.Providers.Redis/        # Redis State (Lua CAS/Tx), Pub/Sub (CloudEvents binary), Locks (Lease renewal)
│   ├── Centra.Providers.PostgreSql/   # PostgreSQL State (ACID table, ETags, Tx, TTL) & Locks (Lease table)
│   ├── Centra.Providers.RabbitMQ/     # RabbitMQ Pub/Sub (AMQP topic exchange, CloudEvents headers, consumer groups)
│   ├── Centra.Providers.SqlServer/    # SQL Server State (MERGE, ETags, Tx, TTL) & Locks (Lease table renewal)
│   ├── Centra.Providers.AzureServiceBus/# Azure Service Bus Pub/Sub (Topics, Subscriptions, CloudEvents headers, DLQ)
│   ├── Centra.Providers.CosmosDb/     # Azure Cosmos DB State (Point reads, TransactionalBatch, ETags, TTL) & Locks
│   ├── Centra.Hosting/                # ASP.NET Core minimal APIs, hosted services, composable DI extensions
│   ├── Centra.ControlPlane/           # Central component catalog, secret resolver, topology tracker, SSE sync dispatcher
│   └── Centra.Aspire.Hosting/         # .NET Aspire AppHost integration, resource mapping extensions
├── samples/
│   ├── Centra.Sample.OrdersService/   # Real-world ASP.NET Core sample microservice
│   ├── Centra.Sample.MultiInstance/   # Multi-instance cluster: discovery, distributed locks, shared state, pub/sub
│   └── Centra.AppHost/                # .NET Aspire cloud-native AppHost orchestrator
└── tests/
    ├── Centra.Tests.Unit/             # Core, runtime & composable DI unit tests
    ├── Centra.ControlPlane.Tests.Unit/# Control Plane unit tests
    ├── Centra.Providers.*.Tests.Unit/ # Provider-specific unit test suites
    └── Centra.Tests.Integration/      # End-to-end workflows & Testcontainers integration tests
```

---

## 🔌 3. Driver SPI Contracts

Centra decouples domain APIs from physical infrastructure via clean Driver SPI contracts defined in [`Centra.Core/Drivers`](file:///home/chad/source/dotnet/distributed-framework/src/Centra.Core/Drivers):

- [`IStateStoreDriver`](file:///home/chad/source/dotnet/distributed-framework/src/Centra.Core/Drivers/IStateStoreDriver.cs): Read, write, delete, and atomic transaction batches with optimistic concurrency (ETags) and TTL.
- [`IPubSubDriver`](file:///home/chad/source/dotnet/distributed-framework/src/Centra.Core/Drivers/IPubSubDriver.cs): Topic publishing and subscription dispatch with CloudEvents binary/structured packing.
- [`IDistributedLockDriver`](file:///home/chad/source/dotnet/distributed-framework/src/Centra.Core/Drivers/IDistributedLockDriver.cs): Lease acquisition, heartbeats, and atomic release.
- [`IBindingDriver`](file:///home/chad/source/dotnet/distributed-framework/src/Centra.Core/Drivers/IBindingDriver.cs): Input/output external system bindings.

---

## 🛠️ 4. Dependency Injection & Configuration Patterns

### Composable Modular Registration (Interface Segregation)

Choose between full-stack registration or fine-grained modular components:

```csharp
// Full-Stack Registration
builder.Services.AddCentra(options =>
{
    options.AppId = "orders-service";
    options.DefaultStateStore = "statestore";
    options.DefaultPubSub = "pubsub";
    options.DefaultLockStore = "lockstore";
});

// Composable Modular Registration (Only what you need!)
builder.Services.AddCentraPubSub();
builder.Services.AddCentraState();
builder.Services.AddCentraLocks();
builder.Services.AddCentraInvocation();
builder.Services.AddCentraBindings();

// Add Provider Implementations (Zero-dependency in-memory or production drivers)
builder.Services.AddCentraInMemory();
// Or: builder.Services.AddCentraRedis(options => options.Configuration = "localhost:6379");
// Or: builder.Services.AddCentraPostgreSql(options => options.ConnectionString = "...");
// Or: builder.Services.AddCentraRabbitMQ(options => options.HostName = "localhost");
// Or: builder.Services.AddCentraSqlServer(options => options.ConnectionString = "...");
// Or: builder.Services.AddCentraAzureServiceBus(options => options.ConnectionString = "...");
// Or: builder.Services.AddCentraCosmosDb(options => options.ConnectionString = "...");

// Register typed RPC client interface
builder.Services.AddCentraServiceClient<IInventoryClient>();

// Register topic subscriber
builder.Services.AddCentraEventHandler<PaymentNotificationHandler, OrderCreatedEvent>();

var app = builder.Build();

// Mount CloudEvents and Bindings route dispatcher
app.MapCentraEndpoints();

app.Run();
```

---

## 🎯 5. Pure Domain Programming Models

### 1. Strongly Typed RPC Client Proxies
No HTTP client boilerplate. Interfaces decorate methods with route metadata:

```csharp
[ServiceClient("inventory-service")]
public interface IInventoryClient
{
    [ServiceMethod("items/check-stock", "POST")]
    Task<bool> CheckStockAsync(string productId, int quantity);
}
```

### 2. Pure Event Handlers (CNCF CloudEvents)
CloudEvent unpacking, correlation ID extraction, and W3C tracecontext propagation occur automatically:

```csharp
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

### 3. Distributed Mutual Exclusion (Locks)
```csharp
await using var @lock = await lockProvider.AcquireLockAsync(
    storeName: "lockstore", 
    resourceId: request.ProductId, 
    expiryTime: TimeSpan.FromSeconds(30), 
    timeout: TimeSpan.FromSeconds(5));

if (!@lock.Success)
{
    return Results.Conflict("Could not acquire lock on product");
}
```

---

## 🚀 6. Zero-Allocation & Performance Guidelines

1. **Prefer `ValueTask` / `ValueTask<T>`** for high-frequency runtime contracts that frequently complete synchronously (e.g. cache hits, in-memory buffers).
2. **Use `readonly record struct`** for immutable data carriers and event DTOs where heap allocations must be minimized.
3. **Buffer Management**: Always use `ArrayPool<byte>.Shared` or [`PooledByteBufferWriter`](file:///home/chad/source/dotnet/distributed-framework/src/Centra.Core/Memory/PooledByteBufferWriter.cs) when serializing or framing CloudEvents and network payloads.
4. **ETags & Optimistic Concurrency**: Never perform blind overwrites on concurrent state. Use `TrySetAsync` with the acquired ETag.
5. **Context Propagation**: Always propagate W3C `traceparent` and `tracestate` across message headers and HTTP calls using `CentraTracePropagator`.

---

## 📜 7. Engineering Invariants & Coding Rules

- **SOLID Principles**: Strict adherence to ISP and DIP across all abstraction libraries.
- **Single Responsibility (File per Type)**: Exactly one type (class, struct, interface, enum) per `.cs` file.
- **Nullability & Warnings**: `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` are globally enforced. Do not suppress warnings without explicit rationale.
- **No Private Instance or Static Methods**: Do not use private instance or static methods in classes or structs. Factor distinct sub-operations into dedicated, single-purpose collaborator types (internal or public, strictly 1 type per file) or inline simple logic directly at the call site.
- **Central Package Management**: Never add `<PackageReference Version="...">` directly to project files. Add versions to [`Directory.Packages.props`](file:///home/chad/source/dotnet/distributed-framework/Directory.Packages.props).
- **TDD (Test-Driven Development)**: All driver implementations and abstractions must be accompanied by unit tests and, where appropriate, Testcontainers integration tests.

---

## 🧪 8. Build & Test Commands

```bash
# Build the complete solution
dotnet build Centra.slnx

# Run all unit tests
dotnet test Centra.slnx --filter "Category!=Integration"

# Run all unit and integration tests (requires Docker for Testcontainers)
dotnet test Centra.slnx --logger "console;verbosity=normal"

# Run the 3-node in-process simulation
dotnet run --project samples/Centra.Sample.MultiInstance -- --demo

# Run with .NET Aspire orchestration
dotnet run --project samples/Centra.AppHost
```
