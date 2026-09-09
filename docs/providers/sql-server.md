# SQL Server Provider

> Enterprise Microsoft SQL Server provider for State Store (Atomic `MERGE` upserts, ETags, `SqlTransaction` batches, TTL) and Distributed Locks (lease tables) via `Microsoft.Data.SqlClient`.

---

## 📦 Package

```xml
<PackageReference Include="Centra.Providers.SqlServer" />
```

---

## 🛠️ Registration

In `Program.cs`:

```csharp
using Centra.Providers.SqlServer.Extensions;

builder.Services.AddCentraSqlServer(options =>
{
    options.ConnectionString = "Server=localhost,1433;Database=centra;User Id=sa;Password=Your_password123;TrustServerCertificate=True;";
    options.SchemaName = "dbo";
    options.AutoCreateTable = true;
});
```

---

## 🔍 Implementation Highlights

### 1. Atomic `MERGE` Upserts with Optimistic Concurrency
State mutations use SQL Server's atomic `MERGE` statement:
```sql
MERGE dbo.CentraStateStore AS target
USING (SELECT @StoreName AS StoreName, @Key AS [Key]) AS src
ON (target.StoreName = src.StoreName AND target.[Key] = src.[Key])
WHEN MATCHED AND (target.ETag = @ExpectedETag OR @ExpectedETag IS NULL) THEN
    UPDATE SET Value = @Value, ETag = @NewETag, ExpiresAt = @ExpiresAt
WHEN NOT MATCHED THEN
    INSERT (StoreName, [Key], Value, ETag, ExpiresAt)
    VALUES (@StoreName, @Key, @Value, @NewETag, @ExpiresAt);
```

### 2. Multi-Operation Batches
Transaction batches run inside a `SqlTransaction` with `IsolationLevel.ReadCommitted` or `RepeatableRead`, rolling back atomically if any single ETag in the batch fails comparison.

### 3. Distributed Lease Locks
The lock table (`CentraDistributedLocks`) uses atomic conditional updates:
```sql
UPDATE dbo.CentraDistributedLocks
SET LockId = @LockId, ExpiresAt = @ExpiresAt
WHERE StoreName = @StoreName AND ResourceId = @ResourceId AND ExpiresAt < SYSUTCDATETIME();
```
Heartbeats renew the lease periodically using the unique `LockId`.
