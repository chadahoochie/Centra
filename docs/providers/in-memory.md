# In-Memory Provider

> Zero-dependency in-memory implementation of State, Pub/Sub, Distributed Locks, and Output Bindings for unit testing and fast local prototyping.

---

## 📦 Package

```xml
<PackageReference Include="Centra.Providers.InMemory" />
```

---

## 🛠️ Registration

In `Program.cs` or test fixture:

```csharp
using Centra.Providers.InMemory.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCentra();

// Registers InMemory State, Pub/Sub, Locks, and Output Bindings
builder.Services.AddCentraInMemory();
```

---

## 🔍 Features Supported

| Component | In-Memory Implementation |
| :--- | :--- |
| **State Store** | Thread-safe concurrent dictionary with ETag versioning, atomic transaction batch support, and TTL expiration pruning. |
| **Pub/Sub** | In-process pub/sub event channel routing CloudEvents directly to registered subscribers. |
| **Distributed Locks** | Async-lock lease mechanism simulating lease acquisition timeouts and disposal. |
| **Bindings** | Memory-based output binding sink for testing webhook emissions. |

---

## 🧪 Use in Unit & Component Tests

Because the In-Memory provider has zero external dependencies (no Docker, no Redis, no cloud credentials), test startup is sub-millisecond:

```csharp
[Fact]
public async Task Should_Persist_And_Read_State_In_Memory()
{
    var services = new ServiceCollection();
    services.AddCentra();
    services.AddCentraInMemory();

    await using var provider = services.BuildServiceProvider();
    var stateStore = provider.GetRequiredService<IStateStore<Order>>();

    var order = new Order("ord-1", "cust-1", "prod-1", 2, "Pending");
    await stateStore.SetAsync(order.Id, order);

    var retrieved = await stateStore.GetAsync(order.Id);
    Assert.True(retrieved.HasValue);
    Assert.Equal("Pending", retrieved.Value.Value.Status);
}
```
