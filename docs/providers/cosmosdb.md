# Azure Cosmos DB Provider

> Globally distributed Azure Cosmos DB NoSQL provider for State Store (Direct mode point reads, `TransactionalBatch` single-partition ACID, ETags, TTL) and Distributed Locks via `Microsoft.Azure.Cosmos`.

---

## 📦 Package

```xml
<PackageReference Include="Centra.Providers.CosmosDb" />
```

---

## 🛠️ Registration

In `Program.cs`:

```csharp
using Centra.Providers.CosmosDb.Extensions;

builder.Services.AddCentraCosmosDb(options =>
{
    options.ConnectionString = "AccountEndpoint=https://your-account.documents.azure.com:443/;AccountKey=...;";
    options.DatabaseName = "centra";
    options.AutoCreateDatabaseAndContainers = true;
});
```

---

## 🔍 Implementation Highlights

### 1. Direct Mode Point Reads & Writes
- Configured with `ConnectionMode.Direct` for minimal latency.
- State documents are modeled as `CosmosStateDocument` with `id` (key) and `partitionKey` (storeName).
- Reads use Cosmos DB point reads (`ReadItemAsync<T>`), consuming only 1 RU per 1KB read.

### 2. Optimistic Concurrency with Cosmos ETags
- Updates pass `ItemRequestOptions { IfMatchEtag = etag }`.
- If another process updated the document, Cosmos DB returns HTTP `412 Precondition Failed`, which Centra maps to an optimistic concurrency conflict.

### 3. Single-Partition `TransactionalBatch`
- Transaction batches targeting the same store execute via Cosmos DB `TransactionalBatch`.
- All operations succeed or fail atomically within the partition.

### 4. Distributed Locks
- Locks are persisted to a `locks` container with document TTL matching the lease expiry duration.
- Heartbeats invoke patch operations updating the lease timestamp.
