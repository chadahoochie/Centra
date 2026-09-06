# Centra: Distributed Application Framework for .NET 10

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512bd4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-29%20Passed-brightgreen.svg)]()

> A modern, cloud-native distributed application framework for .NET 10 inspired by Dapr, engineered natively in C# to eliminate sidecar latency, unify component governance with **Centralized Component Management**, ensure **Observability is a core tenant**, standardize messaging on **CNCF CloudEvents v1.0**, and allow developers to write pure business logic where **"code is focused on code"**.

---

## 🏛️ Key Architectural Pillars

1. **Native In-Process Performance (Zero Sidecars)**:
   - Eliminates sidecar loopback HTTP/gRPC serialization hops and process overhead.
   - High-throughput in-process pipeline using `ValueTask`, `readonly record struct`, `ReadOnlyMemory<byte>`, and `ArrayPool<byte>`.
2. **Centralized Component Management**:
   - Eliminates fragmented, desynchronized YAML component files and Kubernetes CRD drift.
   - Components (State Stores, Pub/Sub Brokers, Distributed Locks, and Bindings) are managed, versioned, and monitored centrally.
3. **Observability as a Core Tenant**:
   - **Distributed Tracing**: Built directly on `System.Diagnostics.ActivitySource("Centra", "1.0.0")` with W3C `DistributedContextPropagator` context propagation (`traceparent`, `tracestate`).
   - **Semantic Span Roles**: Proper `ActivityKind` assignment (`Producer` on pub, `Consumer` on sub, `Client` on RPC invoke, `Server` on RPC handle, `Internal` on state/locks).
   - **Standard Metrics**: Built on `System.Diagnostics.Metrics.Meter("Centra", "1.0.0")` reporting operation counters, latency histograms, and active gauges.
   - **Zero-Allocation Logging**: High-performance `[LoggerMessage]` source generators.
4. **CNCF CloudEvents v1.0 Standard**:
   - Transparently wraps and unwraps domain records into CloudEvents v1.0.
   - Supports **Binary Mode** (default zero-allocation: raw body + `ce-*` headers) and **Structured Mode** (single JSON document).
   - Automatically tracks enterprise extensions: `ce-correlationid`, `ce-causationid`, `ce-tenantid`, `ce-schemaversion`.
5. **Code Focused on Code**:
   - Domain developers interact with clean, strongly typed interfaces (`IStateStore<T>`, `IPubSubClient`, `IDistributedLockProvider`, typed RPC clients) without vendor plumbing.

---

## 🚀 Quickstart: Code Focused on Code

### 1. Register Centra in ASP.NET Core (`Program.cs`)

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Centra with default store names
builder.Services.AddCentra(options =>
{
    options.AppId = "orders-service";
    options.DefaultStateStore = "statestore";
    options.DefaultPubSub = "pubsub";
    options.DefaultLockStore = "lockstore";
});

// Add In-Memory Provider for lightning-fast zero-dependency local dev & testing
builder.Services.AddCentraInMemory();

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

## 🧪 Testing

The solution includes comprehensive unit and integration test suites:

```bash
# Build the entire solution
dotnet build Centra.slnx

# Run all unit and integration tests (29 tests)
dotnet test Centra.slnx --logger "console;verbosity=normal"
```

---

## 📦 Solution Architecture

```
Centra.slnx
├── src/
│   ├── Centra.Abstractions/           # Dependency-free contracts, CloudEvents, ISP interfaces
│   ├── Centra.Core/                   # In-process runtime, zero-alloc serialization, diagnostics, RPC proxies
│   ├── Centra.Providers.InMemory/     # Zero-dependency in-memory driver implementations
│   └── Centra.Hosting/                # ASP.NET Core minimal APIs, hosted services, DI extensions
├── samples/
│   └── Centra.Sample.OrdersService/   # Real-world ASP.NET Core sample microservice
└── tests/
    ├── Centra.Tests.Unit/             # TDD test suites (xUnit, Shouldly, AutoFixture, NSubstitute)
    └── Centra.Tests.Integration/      # End-to-end distributed workflow integration tests
```

---

## 📜 Engineering Invariants
- **SOLID**: Interface segregation (ISP), single responsibility (SRP), dependency inversion (DIP).
- **TDD**: Red/Green/Refactor test-first development.
- **File per Type**: Exactly one type per `.cs` file.
- **Zero Allocation**: `ValueTask`, `readonly record struct`, `ReadOnlyMemory<byte>`, `ArrayPool<byte>`.
- **Standards Compliant**: CNCF CloudEvents v1.0, W3C TraceContext, OpenTelemetry semantic conventions.
