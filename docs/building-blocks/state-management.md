# State Management & Optimistic Concurrency

> Centra provides a strongly-typed key/value state persistence abstraction with optimistic concurrency control (ETags), atomic multi-operation transaction batches, and time-to-live (TTL).

---

## 🎯 Key Interfaces

- **`IStateStore<T>`**: Strongly typed state client for a single entity type.
- **`IStateStore`**: Binary / low-level state client operating over `ReadOnlyMemory<byte>`.
- **`ITransactionalStateStore`**: Transactional batch execution across multiple keys within a single store.

---

## 🛠️ Registration

In `Program.cs`:

```csharp
// Register state building blocks
builder.Services.AddCentraState();

// Register provider (e.g. Redis, PostgreSQL, SQL Server, Cosmos DB, or InMemory)
builder.Services.AddCentraRedis(options => options.Configuration = "localhost:6379");
```

To bind a specific store name to a type:

```csharp
builder.Services.AddCentraStateStore<Order>("orders-store");
```

---

## 📖 Basic Operations

Inject `IStateStore<T>` directly into minimal API routes, background jobs, or handlers:

```csharp
app.MapGet("/orders/{id}", async (string id, IStateStore<Order> stateStore) =>
{
    var entry = await stateStore.GetAsync(id);
    return entry.HasValue ? Results.Ok(entry.Value.Value) : Results.NotFound();
});

app.MapPost("/orders", async (Order order, IStateStore<Order> stateStore) =>
{
    // Save state entry with optional TTL
    var options = new StateOptions(Ttl: TimeSpan.FromDays(30));
    await stateStore.SetAsync(order.Id, order, options);
    return Results.Created($"/orders/{order.Id}", order);
});

app.MapDelete("/orders/{id}", async (string id, IStateStore<Order> stateStore) =>
{
    await stateStore.DeleteAsync(id);
    return Results.NoContent();
});
```

---

## 🔒 Optimistic Concurrency Control (ETags)

To prevent lost updates in high-concurrency environments, use `TrySetAsync` with the ETag retrieved during the read:

```csharp
app.MapPost("/orders/{id}/cancel", async (string id, IStateStore<Order> stateStore) =>
{
    var existing = await stateStore.GetAsync(id);
    if (!existing.HasValue)
    {
        return Results.NotFound();
    }

    var order = existing.Value.Value;
    var etag = existing.Value.ETag;

    var updated = order with { Status = "Cancelled" };

    // TrySetAsync returns false if another process updated the record concurrently
    var success = await stateStore.TrySetAsync(id, updated, etag);
    if (!success)
    {
        return Results.Conflict("Order was modified concurrently. Please retry.");
    }

    return Results.Ok(updated);
});
```

---

## ⚡ Atomic Multi-Key Transactions

For operations that must update or delete multiple keys atomically, cast `IStateStore` to `ITransactionalStateStore`:

```csharp
app.MapPost("/orders/transfer", async (
    string sourceId, 
    string targetId, 
    IStateStore stateStore) =>
{
    if (stateStore is ITransactionalStateStore txStore)
    {
        var operations = new StateTransactionOperation[]
        {
            new SetTransactionOperation("orders", sourceId, sourceBytes, "etag-1"),
            new SetTransactionOperation("orders", targetId, targetBytes, "etag-2"),
            new DeleteTransactionOperation("orders", "audit-temp")
        };

        var committed = await txStore.ExecuteTransactionAsync("orders", operations);
        return committed ? Results.Ok() : Results.Conflict("Transaction failed due to ETag mismatch.");
    }

    return Results.BadRequest("Underlying store does not support transactions.");
});
```

---

## ⏱️ Time-To-Live (TTL)

Set automatic expiration on records via `StateOptions`:

```csharp
var options = new StateOptions(
    Concurrency: ConcurrencyMode.FirstWriteWins,
    Ttl: TimeSpan.FromMinutes(15));

await stateStore.SetAsync("session-123", sessionData, options);
```
Centra translates TTL directly into native driver mechanisms (e.g. `EXPIRE` in Redis, container TTL in Cosmos DB, or TTL indexes in SQL/PostgreSQL).
