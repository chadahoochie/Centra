# PostgreSQL Provider

> Enterprise PostgreSQL integration for State Store (JSONB tables, ACID transactions, ETags, TTL) and Distributed Locks (lease table heartbeats) via `Npgsql`.

---

## 📦 Package

```xml
<PackageReference Include="Centra.Providers.PostgreSql" />
```

---

## 🛠️ Registration

In `Program.cs`:

```csharp
using Centra.Providers.PostgreSql.Extensions;

// Register PostgreSQL for State and Distributed Locks:
builder.Services.AddCentraPostgreSql(options =>
{
    options.ConnectionString = "Host=localhost;Database=centra;Username=postgres;Password=postgres";
    options.Schema = "centra";
    options.AutoCreateSchema = true;
});
```

---

## 🔍 Implementation Highlights

### 1. State Store (`PostgreSqlStateStoreDriver`)
- Stores state in a dedicated schema-isolated table (`centra.state_entries`):
  ```sql
  CREATE TABLE IF NOT EXISTS centra.state_entries (
      store_name TEXT NOT NULL,
      key TEXT NOT NULL,
      value BYTEA NOT NULL,
      etag TEXT NOT NULL,
      expires_at TIMESTAMPTZ NULL,
      PRIMARY KEY (store_name, key)
  );
  ```
- **ETag CAS Writes**:
  ```sql
  UPDATE centra.state_entries 
  SET value = @value, etag = @newEtag, expires_at = @expiresAt
  WHERE store_name = @store AND key = @key AND etag = @expectedEtag;
  ```
- **ACID Transaction Batches**: Operations within a transaction batch run inside a single `NpgsqlTransaction` with strict rollback on ETag collision.
- **TTL Pruning**: Automatic filtering of records where `expires_at <= NOW()`.

### 2. Distributed Locks (`PostgreSqlDistributedLockDriver`)
- Uses a mutual exclusion table (`centra.distributed_locks`):
  ```sql
  CREATE TABLE IF NOT EXISTS centra.distributed_locks (
      store_name TEXT NOT NULL,
      resource_id TEXT NOT NULL,
      lock_id TEXT NOT NULL,
      expires_at TIMESTAMPTZ NOT NULL,
      PRIMARY KEY (store_name, resource_id)
  );
  ```
- **Lease Acquisition**: Inserts or updates if the existing lease has expired:
  ```sql
  INSERT INTO centra.distributed_locks (store_name, resource_id, lock_id, expires_at)
  VALUES (@store, @resource, @lockId, @expiresAt)
  ON CONFLICT (store_name, resource_id) DO UPDATE
  SET lock_id = @lockId, expires_at = @expiresAt
  WHERE centra.distributed_locks.expires_at < NOW();
  ```
- Background heartbeat timer continually updates `expires_at` while the lease is held.
