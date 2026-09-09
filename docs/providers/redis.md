# Redis Provider

> High-performance Redis integration for State Store (atomic Lua CAS), Pub/Sub (CNCF CloudEvents binary framing), and Distributed Locks (lease heartbeats) via `StackExchange.Redis`.

---

## 📦 Package

```xml
<PackageReference Include="Centra.Providers.Redis" />
```

---

## 🛠️ Registration

In `Program.cs`:

```csharp
using Centra.Providers.Redis.Extensions;

// Register Redis for State, Pub/Sub, and Distributed Locks:
builder.Services.AddCentraRedis(options =>
{
    options.Configuration = "localhost:6379";
    options.InstanceName = "app:"; // Key prefix
    options.DefaultDatabase = 0;
});
```

### Granular Registration
If you only want Redis for a specific concern:

```csharp
// Only State Store:
builder.Services.AddCentraRedisStateStore("statestore", options => options.Configuration = "localhost:6379");

// Only Pub/Sub:
builder.Services.AddCentraRedisPubSub("pubsub", options => options.Configuration = "localhost:6379");

// Only Distributed Locks:
builder.Services.AddCentraRedisDistributedLocks("lockstore", options => options.Configuration = "localhost:6379");
```

---

## 🔍 Implementation Highlights

### 1. State Store (Lua CAS)
- Redis key structure: `{prefix}{storeName}:{key}`
- Stores value and metadata in a Redis hash (`data`, `etag`, `expires_at`).
- Optimistic concurrency updates execute via an **atomic Lua script**:
  ```lua
  local current = redis.call('HGET', KEYS[1], 'etag')
  if current == ARGV[1] then
      redis.call('HSET', KEYS[1], 'data', ARGV[2], 'etag', ARGV[3])
      return 1
  else
      return 0
  end
  ```
- Supports multi-key transactional batches using Redis transactions (`MULTI` / `EXEC`).

### 2. Pub/Sub (CloudEvents Binary Framing)
- Maps topics directly to Redis Pub/Sub channels or Redis Streams.
- Transmits raw payload bytes alongside `ce-*` headers in an optimized binary envelope.

### 3. Distributed Locks (Lease Heartbeats)
- Uses `SET resourceId lockId NX PX {expiryMs}`.
- Spawns a background renewal loop running an atomic Lua script:
  ```lua
  if redis.call('GET', KEYS[1]) == ARGV[1] then
      return redis.call('PEXPIRE', KEYS[1], ARGV[2])
  else
      return 0
  end
  ```
- Atomic release deletes the key only if the token matches, preventing accidental release of acquired locks after timeout.
