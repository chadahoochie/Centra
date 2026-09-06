---
name: redis-dotnet
description: Enforces architectural standards for .NET applications leveraging Redis as a multi-model primary database, data structure server, and cache. Activate this skill when writing, refactoring, or reviewing C#/.NET code that interacts with Redis.
---

# Redis & .NET Architectural Standards

This instruction set enforces our .NET and Redis architectural standards regarding client usage, data modeling, concurrency, and persistence.

## 1. Client Library Selection & Setup
*   **Core Client:** Use `StackExchange.Redis` as the foundational client for standard Redis commands and data types. Always utilize its asynchronous API methods (e.g., `StringSetAsync`, `HashGetAllAsync`) to avoid thread-pool starvation.
*   **Extended Capabilities:** When interacting with Redis Stack modules (such as JSON, Search, TimeSeries, Bloom Filters, or Top-K), you must use the `NRedisStack` NuGet package, which builds upon `StackExchange.Redis`. 
*   **Dialect Awareness:** When writing query commands via `NRedisStack` (version 1.0.0-beta1+), be aware that it automatically appends `DIALECT 2` to commands like `FT.SEARCH` and `FT.AGGREGATE`.

## 2. Data Modeling & In-Place Computation
*   **Use Native Structures:** Do not serialize massive JSON blobs into basic strings if you need to update individual fields. Use Redis Hashes (`HSET`, `HGETALL`) or the RedisJSON module to modify data in place.
*   **Sorted Sets for Ranking:** For leaderboards or top-N scenarios, implement Sorted Sets (`ZADD`, `ZRANGE`) which utilize hash tables and skip lists to sort data natively upon insertion.
*   **Streams for Async Queues:** For reliable, ordered asynchronous job queues, utilize Redis Streams with Consumer Groups to allocate work to distributed workers with at-least-once delivery guarantees.
*   **Geospatial Indexing:** Use Redis's built-in geospatial indexes (`GEOADD`, `GEOSEARCH`) to calculate bounding boxes and distances natively via geohashes.

## 3. Concurrency & Atomicity
*   **Lock-Free Operations:** Rely on Redis's single-threaded nature for concurrency control. If two users concurrently decrement an inventory counter, utilize native atomic commands (e.g., `INCR`, `DECR`) rather than implementing external mutexes or locks.
*   **Rate Limiting:** Implement distributed rate limiters simply by using atomic increments paired with Key expirations (TTL) to throttle requests safely across instances.

## 4. Scaling & Keyspace Management
*   **Hot Key Prevention:** When designing cache keys, proactively append random numbers or tenant IDs to keys to evenly distribute load and prevent single-node saturation (the "hot key" problem).
*   **Sharding Awareness:** Remember that scaling out write throughput relies on hashing the Key to a specific "slot" (using CRC hashing). Design your keyspace deliberately if you need related data to land on the same shard.

## 5. High Availability & Persistence
*   **Durability Configurations:** Configure both RDB (Redis Database snapshots) and AOF (Append Only File). RDB uses a `fork` system call and copy-on-write to safely dump memory to disk without blocking the main thread, while AOF replays logs to prevent data loss between snapshots.
*   **Decouple Storage:** Ensure that persistence files are written to separated, durable network storage (like AWS EBS or Azure Managed Disks) rather than ephemeral host drives.
*   **Enterprise Scaling:** For global, multi-region deployments, leverage Active-Active Geo-Replication which relies on Conflict-free Replicated Data Types (CRDTs) to handle parallel changes without data loss.
*   **Cost Optimization:** If memory limits are a concern for massive datasets, configure "Redis on Flash" (available in Azure Managed Redis Flash optimized tiers or Redis Enterprise) to extend RAM onto NVMe SSDs for less frequently accessed data.
