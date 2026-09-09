# Quickstart: Build Your First Centra Microservice

> In this tutorial, you will build and run an e-commerce microservice with state persistence, CNCF CloudEvents pub/sub, distributed locks, and typed RPC in under 5 minutes.

---

## 🎯 What You'll Build

You will create an **Orders Service** that:
1. Acquires a distributed lock to prevent concurrent inventory race conditions.
2. Invokes an external inventory check via strongly typed RPC without boilerplate HTTP clients.
3. Persists order state with optimistic concurrency control (ETags).
4. Emits a CNCF CloudEvents v1.0 message to publish `OrderCreatedEvent`.
5. Consumes the event asynchronously with ambient W3C distributed tracing context.

---

## 📋 Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Any code editor (VS Code, JetBrains Rider, Visual Studio 2026+)

---

## 🛠️ Step 1: Create the Project

Initialize a new ASP.NET Core Minimal API project:

```bash
dotnet new web -n OrdersMicroservice
cd OrdersMicroservice
```

Add references to Centra packages:

```xml
<!-- In OrdersMicroservice.csproj -->
<ItemGroup>
  <PackageReference Include="Centra.Hosting" />
  <PackageReference Include="Centra.Providers.InMemory" />
</ItemGroup>
```

> [!NOTE]
> During local development and testing, `Centra.Providers.InMemory` provides zero-dependency implementations for State, Pub/Sub, Locks, and Bindings. Switching to production providers (Redis, RabbitMQ, PostgreSQL, etc.) requires only changing a single registration line in `Program.cs`.

---

## 📝 Step 2: Define Domain Models & Contracts

Create the records and interfaces for your domain. Centra encourages "code focused on code" where models are pure C# records:

```csharp
// Domain/Models.cs
namespace OrdersMicroservice.Domain;

public readonly record struct CreateOrderRequest(
    string CustomerId,
    string ProductId,
    int Quantity);

public sealed record Order(
    string Id,
    string CustomerId,
    string ProductId,
    int Quantity,
    string Status);

public readonly record struct OrderCreatedEvent(
    string OrderId,
    string CustomerId,
    string ProductId);
```

### Typed RPC Client
Decorate an interface with `[ServiceClient]` and `[ServiceMethod]`. Centra dynamically generates an in-process proxy that handles serialization, HTTP routing, and resilience without any HTTP client plumbing:

```csharp
// Services/IInventoryClient.cs
using Centra.Invocation;

namespace OrdersMicroservice.Services;

[ServiceClient("inventory-service")]
public interface IInventoryClient
{
    [ServiceMethod("items/check-stock", "POST")]
    Task<bool> CheckStockAsync(string productId, int quantity);
}
```

### Event Handler
Implement `IEventHandler<T>` and decorate with `[Topic]`. Centra's runtime automatically extracts the CloudEvent payload, establishes W3C tracecontext parentage, and handles errors:

```csharp
// Handlers/PaymentNotificationHandler.cs
using Centra.Events;
using Centra.PubSub;
using Centra.State;
using OrdersMicroservice.Domain;

namespace OrdersMicroservice.Handlers;

[Topic("pubsub", "orders.created")]
public sealed class PaymentNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IStateStore<Order> _stateStore;
    private readonly ILogger<PaymentNotificationHandler> _logger;

    public PaymentNotificationHandler(
        IStateStore<Order> stateStore, 
        ILogger<PaymentNotificationHandler> logger)
    {
        _stateStore = stateStore;
        _logger = logger;
    }

    public async Task<EventHandlingResult> HandleAsync(
        OrderCreatedEvent @event,
        EventContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing payment for Order {OrderId} (CorrelationId: {CorrId})",
            @event.OrderId, context.CorrelationId);

        var existing = await _stateStore.GetAsync(@event.OrderId, cancellationToken: cancellationToken);
        if (existing.HasValue)
        {
            var updated = existing.Value.Value with { Status = "Paid" };
            await _stateStore.TrySetAsync(@event.OrderId, updated, existing.Value.ETag, cancellationToken: cancellationToken);
        }

        return EventHandlingResult.Success;
    }
}
```

---

## 🔌 Step 3: Wire Centra in `Program.cs`

Centra supports two configuration models: **Declarative via Attributes** (with automatic reflection scanning) and **Programmatic via `IServiceCollection` Extensions** (for explicit wiring and overrides).

When you invoke `builder.Services.AddCentra()`, Centra automatically scans your assembly and registers all classes and interfaces decorated with attributes (like `[Topic]` on `PaymentNotificationHandler` and `[ServiceClient]` on `IInventoryClient`). You can also invoke explicit extension methods (such as `AddCentraServiceClient<IInventoryClient>()` or `AddCentraEventHandler<THandler, TEvent>()`) to override attribute settings or wire components selectively.

```csharp
// Program.cs
using Centra.Hosting.Extensions;
using Centra.Locks;
using Centra.PubSub;
using Centra.State;
using OrdersMicroservice.Domain;
using OrdersMicroservice.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Full-Stack Registration & Automatic Attribute Discovery:
// Scans entry assembly for [Topic], [ServiceClient], [Actor], [Workflow], etc.
builder.Services.AddCentra(options =>
{
    options.AppId = "orders-service";
    options.DefaultStateStore = "statestore";
    options.DefaultPubSub = "pubsub";
    options.DefaultLockStore = "lockstore";
});

// 2. Add In-Memory providers for testing (or Redis, RabbitMQ, PostgreSQL, etc.)
builder.Services.AddCentraInMemory();

// 3. (Optional) Explicit Service Collection Extensions:
// Can be used to explicitly register clients or override attribute values at runtime.
// If omitted, AddCentra() above already auto-discovered IInventoryClient via [ServiceClient].
builder.Services.AddCentraServiceClient<IInventoryClient>();

var app = builder.Build();

// 4. Map domain minimal API endpoint
app.MapPost("/orders", async (
    CreateOrderRequest request,
    IStateStore<Order> stateStore,
    IPubSubClient pubSub,
    IDistributedLockProvider lockProvider) =>
{
    // A. Distributed mutual exclusion lock on product
    await using var @lock = await lockProvider.AcquireLockAsync(
        storeName: "lockstore",
        resourceId: request.ProductId,
        expiryTime: TimeSpan.FromSeconds(30),
        timeout: TimeSpan.FromSeconds(5));

    if (!@lock.Success)
    {
        return Results.Conflict("Could not acquire lock for inventory update.");
    }

    // B. Persist state with optimistic concurrency
    var orderId = $"ord-{Guid.NewGuid():N}"[..12];
    var order = new Order(orderId, request.CustomerId, request.ProductId, request.Quantity, "Created");
    await stateStore.SetAsync(order.Id, order);

    // C. Publish CNCF CloudEvent v1.0
    await pubSub.PublishAsync("orders.created", new OrderCreatedEvent(order.Id, order.CustomerId, order.ProductId));

    return Results.Created($"/orders/{order.Id}", order);
});

// 5. Mount CloudEvents and Bindings route dispatcher
app.MapCentraEndpoints();

app.Run();
```

---

## 🏃 Step 4: Run and Test

Start the microservice:

```bash
dotnet run
```

Submit an order via `curl`:

```bash
curl -X POST http://localhost:5000/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "cust-42",
    "productId": "sku-keyboard-99",
    "quantity": 1
  }'
```

You will receive an HTTP `201 Created` response:

```json
{
  "id": "ord-7f1a9b2c3d4e",
  "customerId": "cust-42",
  "productId": "sku-keyboard-99",
  "quantity": 1,
  "status": "Created"
}
```

In the application console logs, you will observe:
1. `Lock acquired for resource 'sku-keyboard-99'`.
2. State persisted to store `statestore`.
3. CloudEvent published to topic `orders.created`.
4. `PaymentNotificationHandler` automatically invoked, logging correlation ID and updating state status to `"Paid"`.
5. Distributed lock cleanly released.

---

## 🚀 Next Steps

- Explore how to run multi-replica clusters with [.NET Aspire Orchestration](aspire.md).
- Learn how to run a full 3-node containerized stack with [Docker Compose Cluster](docker-cluster.md).
- Dive into [Virtual Actors](../building-blocks/virtual-actors.md) and [Workflows & Sagas](../building-blocks/workflows-and-sagas.md).
