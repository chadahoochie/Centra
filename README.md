# Centra: Distributed Application Framework for .NET 10

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512bd4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-77%20Passed-brightgreen.svg)]()

> A modern, cloud-native distributed application framework for .NET 10 inspired by Dapr, engineered natively in C# to eliminate sidecar latency, unify component governance with **Centralized Component Management**, ensure **Observability is a core tenant**, standardize messaging on **CNCF CloudEvents v1.0**, and allow developers to write pure business logic where **"code is focused on code"**.

---

## 🏛️ Key Architectural Pillars

1. **Native In-Process Performance (Zero Sidecars)**:
   - Eliminates sidecar loopback HTTP/gRPC serialization hops and process overhead.
   - High-throughput in-process pipeline using `ValueTask`, `readonly record struct`, `ReadOnlyMemory<byte>`, and `ArrayPool<byte>`.
2. **Modular & Pluggable Abstractions (Zero Dependency Drag)**:
   - Fine-grained, decoupled contracts following Interface Segregation Principle (ISP).
   - Consume *only* what you need (e.g. `Centra.PubSub.Abstractions` without dragging State Store or Distributed Locks).
   - Composable DI registrations (`AddCentraPubSub`, `AddCentraState`, `AddCentraLocks`, `AddCentraInvocation`, `AddCentraBindings`, or full-stack `AddCentra`).
3. **Centralized Component Management**:
   - Eliminates fragmented, desynchronized YAML component files and Kubernetes CRD drift.
   - Components (State Stores, Pub/Sub Brokers, Distributed Locks, and Bindings) are managed, versioned, and monitored centrally with real-time SSE hot-reloading.
4. **Observability as a Core Tenant**:
   - **Distributed Tracing**: Built directly on `System.Diagnostics.ActivitySource("Centra", "1.0.0")` with W3C `DistributedContextPropagator` context propagation (`traceparent`, `tracestate`).
   - **Semantic Span Roles**: Proper `ActivityKind` assignment (`Producer` on pub, `Consumer` on sub, `Client` on RPC invoke, `Server` on RPC handle, `Internal` on state/locks).
   - **Standard Metrics**: Built on `System.Diagnostics.Metrics.Meter("Centra", "1.0.0")` reporting operation counters, latency histograms, and active gauges.
   - **Zero-Allocation Logging**: High-performance `[LoggerMessage]` source generators.
5. **CNCF CloudEvents v1.0 Standard**:
   - Transparently wraps and unwraps domain records into CloudEvents v1.0.
   - Supports **Binary Mode** (default zero-allocation: raw body + `ce-*` headers) and **Structured Mode** (single JSON document).
   - Automatically tracks enterprise extensions: `ce-correlationid`, `ce-causationid`, `ce-tenantid`, `ce-schemaversion`.
6. **Code Focused on Code**:
   - Domain developers interact with clean, strongly typed interfaces (`IStateStore<T>`, `IPubSubClient`, `IDistributedLockProvider`, typed RPC clients) without vendor plumbing.

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

---

## 🔌 Production Distributed Providers

Centra provides high-performance, production-ready distributed providers with zero sidecars and native C# drivers:

| Provider | Components Supported | Features | Package |
| :--- | :--- | :--- | :--- |
| **Redis** | State Store, Pub/Sub, Distributed Lock | Atomic Lua Compare-And-Swap (ETag validation), multi-key transactions, CloudEvents binary framing, consumer groups, DLQ, lock heartbeat renewal | `Centra.Providers.Redis` |
| **PostgreSQL** | State Store, Distributed Lock | Schema-isolated JSONB state table, ACID transactions, optimistic concurrency with ETags, TTL expiration pruning, mutual exclusion lease table | `Centra.Providers.PostgreSql` |
| **RabbitMQ** | Pub/Sub | AMQP topic exchange, CNCF CloudEvents headers mapping, dead-letter exchanges (DLX), durable queues, competing consumers | `Centra.Providers.RabbitMQ` |

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
```

---

## 🧪 Testing

The solution includes comprehensive unit and integration test suites:

```bash
# Build the entire solution
dotnet build Centra.slnx

# Run all unit and integration tests (77 tests)
dotnet test Centra.slnx --logger "console;verbosity=normal"
```

---

## 📦 Solution Architecture

```
Centra.slnx
├── src/
│   ├── Centra.Abstractions/           # Umbrella metapackage referencing all 8 modular abstractions
│   ├── Centra.Events.Abstractions/    # CNCF CloudEvents v1.0 contracts & ambient context
│   ├── Centra.PubSub.Abstractions/    # IPubSubClient, IEventHandler, Topic contracts
│   ├── Centra.State.Abstractions/     # IStateStore, StateEntry, transactions, optimistic concurrency
│   ├── Centra.Locks.Abstractions/     # IDistributedLockProvider & IDistributedLock contracts
│   ├── Centra.Invocation.Abstractions/# IServiceInvoker & typed RPC client attributes
│   ├── Centra.Bindings.Abstractions/  # IOutputBinding & Cron trigger contracts
│   ├── Centra.Components.Abstractions/# ComponentDefinition, ComponentType, IComponentRegistry
│   ├── Centra.Sync.Abstractions/      # IControlPlaneClient & live streaming sync event DTOs
│   ├── Centra.Core/                   # In-process runtime, zero-alloc serialization, diagnostics, RPC proxies, SSE client
│   ├── Centra.Providers.InMemory/     # Zero-dependency in-memory driver implementations
│   ├── Centra.Providers.Redis/        # Redis State (Lua CAS/Tx), Pub/Sub (CloudEvents v1.0 binary), Locks (Lease/Renewal)
│   ├── Centra.Providers.PostgreSql/   # PostgreSQL State (ACID table, ETags, Tx, TTL) & Locks (Lease table heartbeat)
│   ├── Centra.Providers.RabbitMQ/     # RabbitMQ Pub/Sub (AMQP topic exchange, CloudEvents headers, consumer groups, DLX)
│   ├── Centra.Hosting/                # ASP.NET Core minimal APIs, hosted services, composable DI extensions
│   ├── Centra.ControlPlane/           # Central component catalog, secret resolver, topology tracker, SSE sync dispatcher
│   └── Centra.Aspire.Hosting/         # .NET Aspire AppHost integration, resource mapping extensions
├── samples/
│   ├── Centra.Sample.OrdersService/   # Real-world ASP.NET Core sample microservice
│   └── Centra.AppHost/                # .NET Aspire cloud-native AppHost orchestrator
└── tests/
    ├── Centra.Tests.Unit/             # Core, runtime & composable DI TDD test suites (40 tests)
    ├── Centra.ControlPlane.Tests.Unit/# Control Plane TDD test suites (8 tests)
    ├── Centra.Providers.Redis.Tests.Unit/        # Redis State, Pub/Sub, and Locks unit tests (17 tests)
    ├── Centra.Providers.PostgreSql.Tests.Unit/  # PostgreSQL State and Locks unit tests (2 tests)
    ├── Centra.Providers.RabbitMQ.Tests.Unit/    # RabbitMQ Pub/Sub unit tests (2 tests)
    └── Centra.Tests.Integration/      # End-to-end workflows & Testcontainers integration tests (8 tests)
```

---

## 📜 Engineering Invariants
- **SOLID**: Interface segregation (ISP), single responsibility (SRP), dependency inversion (DIP).
- **TDD**: Red/Green/Refactor test-first development.
- **File per Type**: Exactly one type per `.cs` file.
- **Zero Allocation**: `ValueTask`, `readonly record struct`, `ReadOnlyMemory<byte>`, `ArrayPool<byte>`.
- **Standards Compliant**: CNCF CloudEvents v1.0, W3C TraceContext, OpenTelemetry semantic conventions.
