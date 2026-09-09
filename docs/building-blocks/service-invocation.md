# Service Invocation & Strongly Typed RPC

> Call remote microservices using strongly typed C# interfaces with dynamic proxy dispatch, automatic serialization, client-side load balancing, and W3C distributed tracing.

---

## 🎯 Key Concepts

- **`[ServiceClient]`**: Decorates an interface with the logical target `AppId`.
- **`[ServiceMethod]`**: Decorates interface methods with route template and HTTP verb.
- **Dynamic Proxy Generation**: `ServiceProxyFactory` generates high-performance `DispatchProxy` implementations at runtime.
- **Client-Side Load Balancing**: `ControlPlaneServiceEndpointResolver` queries the Control Plane topology and round-robins across active replicas.

---

## 🛠️ Defining the RPC Contract

Create an interface shared between caller and target:

```csharp
using Centra.Invocation;

namespace MyProject.Contracts;

[ServiceClient("inventory-service")]
public interface IInventoryClient
{
    [ServiceMethod("items/{productId}/check-stock", "GET")]
    Task<StockCheckResponse> CheckStockAsync(string productId);

    [ServiceMethod("items/reserve", "POST")]
    Task<ReservationResult> ReserveItemsAsync(ReserveItemsRequest request);
}
```

---

## ⚙️ Registration in `Program.cs`

Centra provides two ways to register RPC client proxies:

### Option A: Declarative via `[ServiceClient]` Attribute (Automatic Discovery)
Decorate your interface with `[ServiceClient("app-id")]` and call `builder.Services.AddCentra()`:

```csharp
// Registers invocation engine and auto-discovers all [ServiceClient] interfaces in the assembly
builder.Services.AddCentra();
```

Centra's assembly scanner automatically finds decorated interfaces and registers high-performance dynamic proxies into `IServiceCollection` with zero manual wiring.

### Option B: Programmatic via `AddCentraServiceClient` Extension (Explicit Modular Registration)
When adhering strictly to the **Interface Segregation Principle (ISP)** by not referencing the full framework umbrella, or when writing focused integration test harnesses, register the invocation runtime and typed clients explicitly:

```csharp
// 1. Add invocation building blocks only (modular registration)
builder.Services.AddCentraInvocation();

// 2. Explicitly register typed client proxy
builder.Services.AddCentraServiceClient<IInventoryClient>();
```

---

## 🚀 Invoking Remote Services

Inject the interface directly:

```csharp
app.MapPost("/orders", async (
    CreateOrderRequest request, 
    IInventoryClient inventoryClient) =>
{
    // Pure C# call - no HttpClient, Uri construction, or manual JSON parsing
    var stock = await inventoryClient.CheckStockAsync(request.ProductId);
    if (!stock.InStock)
    {
        return Results.BadRequest("Item is out of stock.");
    }

    var reservation = await inventoryClient.ReserveItemsAsync(new ReserveItemsRequest(
        request.ProductId, 
        request.Quantity));

    return Results.Ok(reservation);
});
```

---

## 🌐 Dynamic Endpoint Resolution & Load Balancing

When `inventoryClient.CheckStockAsync(...)` is called:
1. The proxy resolves the `AppId` (`inventory-service`) from the `[ServiceClient]` attribute.
2. The `IServiceEndpointResolver` looks up healthy nodes from `IClusterTopologyProvider`.
3. If multiple replicas exist (e.g. `http://inventory-1:5000` and `http://inventory-2:5000`), requests round-robin uniformly across replicas.
4. If a node fails heartbeats, it is evicted from the topology pool automatically.
5. All calls are wrapped in OpenTelemetry spans (`ActivityKind.Client`) propagating W3C context headers.
