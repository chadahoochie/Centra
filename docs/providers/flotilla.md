# Flotilla Provider

> High-throughput, microsecond-latency Raft consensus pub/sub provider for Centra with strict monotonic total order and CNCF CloudEvents v1.0 header mapping via the Flotilla sans-I/O Raft engine.

---

## 📦 Package

```xml
<PackageReference Include="Centra.Providers.Flotilla" />
<PackageReference Include="Centra.Providers.Flotilla.Hosting" />
```

---

## 🛠️ Registration

In `Program.cs`:

```csharp
using Centra.Providers.Flotilla.Extensions;

builder.Services.AddCentraFlotilla(options =>
{
    options.ClusterNodes = ["127.0.0.1:9001", "127.0.0.1:9002", "127.0.0.1:9003"];
    options.DefaultPubSubName = "integration";
    options.ClientTimeoutMs = 50;
    options.MaxBatchSize = 100;
    options.EnableChecksumVerification = true;
});
```

---

## 🔍 Implementation Highlights

### 1. Sub-80µs Quorum Commit Latency
- Flotilla executes Raft consensus in-memory (< 80µs quorum commit) across UDP framing with IEEE CRC32 verification.
- Replaces AMQP TCP broker hops, channel locking, and disk fsync bottlenecks with zero-allocation consensus.

### 2. Strict Monotonic Total Ordering
- All messages proposed to a topic are sequenced into a monotonic `LogIndex` stream.
- Eliminates race conditions across concurrent consumer nodes.

### 3. CNCF CloudEvents v1.0 Header Mapping
- CloudEvents metadata headers (`ce-id`, `ce-source`, `ce-type`, `ce-specversion`, `traceparent`) are packed directly into the Flotilla wire frame alongside the domain payload.
- Consumed directly by Centra's `IEventHandler<T>` without double serialization.

### 4. Competing Consumer Groups & Watermark Advancement
- Handlers subscribe to topics through `Centra.PubSub.Abstractions` (`IPubSubSubscriber`).
- As consumers acknowledge successful handling (`EventHandlingResult.Success`), the subscription worker advances the durable watermark, allowing Flotilla's ring buffer to safely compact.

### 5. Decoupled Cosmos DB 90-Day Archival
- Flotilla's pluggable `CosmosArchiveSink` directly archives committed entries to Azure Cosmos DB (`ingress-journal-v2`) with hierarchical partition keys (`["/sliceKey", "/dateBucket"]`), removing journal writes from the application ingress path.
