# Flotilla Provider

> High-throughput, microsecond-latency Raft consensus pub/sub provider for Centra with strict monotonic total order and CNCF CloudEvents v1.0 header mapping via the Flotilla sans-I/O Raft engine.
> Supports pluggable transport protocols: **UDP**, **TCP**, and **gRPC**, split across three dedicated modules for zero dependency drag.

---

## 📦 Packages

Choose the package corresponding to your preferred transport protocol:

### UDP Transport (Sub-80µs Commit Latency, Zero External Dependencies)
```xml
<PackageReference Include="Centra.Providers.Flotilla.Udp" />
```

### TCP Transport (Connection-Oriented Streaming, 40-Byte Header Framing)
```xml
<PackageReference Include="Centra.Providers.Flotilla.Tcp" />
```

### gRPC Transport (HTTP/2 Protobuf via FlotillaService RPC)
```xml
<PackageReference Include="Centra.Providers.Flotilla.Grpc" />
```

---

## 🛠️ Registration

### 1. UDP Transport Registration

In `Program.cs`:

```csharp
using Centra.Providers.Flotilla.Udp.Extensions;

builder.Services.AddCentraFlotillaUdp(options =>
{
    options.ClusterNodes = ["127.0.0.1:9001", "127.0.0.1:9002", "127.0.0.1:9003"];
    options.DefaultPubSubName = "consensus-udp";
    options.ClientTimeoutMs = 50;
    options.MaxBatchSize = 100;
    options.EnableChecksumVerification = true;
});
```

### 2. TCP Transport Registration

In `Program.cs`:

```csharp
using Centra.Providers.Flotilla.Tcp.Extensions;

builder.Services.AddCentraFlotillaTcp(options =>
{
    options.ClusterNodes = ["127.0.0.1:9001", "127.0.0.1:9002", "127.0.0.1:9003"];
    options.DefaultPubSubName = "consensus-tcp";
    options.ClientTimeoutMs = 1000;
    options.EnableChecksumVerification = true;
});
```

### 3. gRPC Transport Registration

In `Program.cs`:

```csharp
using Centra.Providers.Flotilla.Grpc.Extensions;

builder.Services.AddCentraFlotillaGrpc(options =>
{
    options.ClusterNodes = ["http://127.0.0.1:9001", "http://127.0.0.1:9002", "http://127.0.0.1:9003"];
    options.DefaultPubSubName = "consensus-grpc";
    options.ClientTimeoutMs = 1000;
});
```

---

## 🔍 Implementation Highlights

### 1. Pluggable Tri-Protocol Transports
- **UDP**: Optimal for low-latency in-datacenter deployments (< 80µs quorum commit) across UDP framing with IEEE CRC32 verification.
- **TCP**: Connection-oriented reliable byte-stream framing with 40-byte packet headers and 32-byte `FlotillaClientProposalReply` responses.
- **gRPC**: Standards-based HTTP/2 protobuf transport using `FlotillaService.Propose` RPCs.

### 2. Strict Monotonic Total Ordering
- All messages proposed to a topic are sequenced into a monotonic `LogIndex` stream regardless of chosen transport.
- Eliminates race conditions across concurrent consumer nodes.

### 3. CNCF CloudEvents v1.0 Header Mapping
- CloudEvents metadata headers (`ce-id`, `ce-source`, `ce-type`, `ce-specversion`, `traceparent`) are packed directly into the Flotilla wire frame alongside the domain payload.
- Consumed directly by Centra's `IEventHandler<T>` without double serialization.

### 4. Competing Consumer Groups & Watermark Advancement
- Handlers subscribe to topics through `Centra.PubSub.Abstractions` (`IPubSubSubscriber`).
- As consumers acknowledge successful handling (`EventHandlingResult.Success`), the subscription worker advances the durable watermark, allowing Flotilla's ring buffer to safely compact.

### 5. Decoupled Cosmos DB 90-Day Archival
- Flotilla's pluggable `CosmosArchiveSink` directly archives committed entries to Azure Cosmos DB (`ingress-journal-v2`) with hierarchical partition keys (`["/sliceKey", "/dateBucket"]`), removing journal writes from the application ingress path.
