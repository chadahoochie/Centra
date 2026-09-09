# Architectural Overview & Design Principles

> Centra is an in-process, cloud-native distributed application framework engineered natively in C# for .NET 10. It eliminates sidecar latency and memory overhead while offering a unified programming model for state, pub/sub, locks, invocation, virtual actors, resilience, and sagas.

---

## 🏛️ Foundational Design Pillars

```
                                  Domain Code
                   ("Code Focused on Code" - Pure C#)
                                       │
      ┌────────────────────────────────┼────────────────────────────────┐
      │                                │                                │
      ▼                                ▼                                ▼
  Abstractions                    Abstractions                     Abstractions
(IStateStore<T>)               (IPubSubClient)             (IActor / IWorkflow)
      │                                │                                │
      ▼                                ▼                                ▼
In-Process Runtime            In-Process Runtime               In-Process Runtime
 (Zero Sidecars)               (Zero Sidecars)                  (Zero Sidecars)
      │                                │                                │
      ▼                                ▼                                ▼
 Driver SPI                       Driver SPI                       Driver SPI
(IStateStoreDriver)              (IPubSubDriver)              (IDistributedLockDriver)
      │                                │                                │
      └────────────────────────────────┼────────────────────────────────┘
                                       ▼
                       Pluggable Physical Infrastructure
                   (Redis, Postgres, RabbitMQ, Cosmos DB)
```

### 1. Native In-Process Performance (Zero Sidecars)
Traditional distributed runtimes (such as Dapr) rely on a separate sidecar process (e.g. written in Go or Rust) communicating over a local loopback HTTP/1.1 or gRPC socket. This introduces:
- Duplicate process memory overhead (typically 30–80 MB per container).
- Two additional serialization/deserialization hops per operation.
- Kernel network stack latency and TCP context switching (typically 1.5–4.0 ms per hop).

Centra eliminates this sidecar boundary. The framework compiles directly into your ASP.NET Core or Worker process as high-performance .NET assemblies, communicating directly with underlying storage drivers in native C#.

### 2. Zero-Allocation Memory Discipline
High-throughput distributed systems can easily trigger excessive garbage collection pressure. Centra enforces strict zero-allocation conventions across hot paths:
- **`ValueTask` / `ValueTask<T>`**: Used throughout internal driver SPIs, actor mailboxes, and cached state reads to avoid `Task` heap allocations when operations complete synchronously.
- **`readonly record struct`**: Used for lightweight parameter objects, event metadata, and DTOs.
- **`ReadOnlyMemory<byte>` & `Span<byte>`**: Binary payloads, CloudEvents headers, and raw state bytes are sliced without heap allocation.
- **`ArrayPool<byte>.Shared` & `PooledByteBufferWriter`**: Dynamic buffer allocation for serialization and network streaming rents from shared pools.

### 3. Modular Abstractions (Interface Segregation Principle)
Centra rejects monolithic dependencies. The framework is decomposed into fine-grained abstraction libraries:
- An application that only requires pub/sub can reference `Centra.PubSub.Abstractions` without dragging dependencies on state stores, locks, actors, or workflows.
- Full-stack applications can use the umbrella metapackage `Centra.Abstractions`.

### 4. Centralized Component Governance
Instead of scattering fragmented YAML component manifests across Kubernetes clusters or git repositories that drift out of sync, Centra components are managed centrally via the **Centra Control Plane**:
- Component definitions (connection strings, secrets, options) are registered via REST APIs.
- Dynamic updates stream to running application replicas in real-time over Server-Sent Events (SSE).
- Application instances adapt configurations without restarting pods.

### 5. CNCF CloudEvents v1.0 Standard
All inter-service messaging conforms strictly to the **CNCF CloudEvents v1.0** specification:
- **Binary Mode (Default)**: Event payload stays raw bytes; CloudEvents metadata is mapped directly into transport headers (`ce-id`, `ce-source`, `ce-type`, `ce-specversion`). Zero-allocation serialization.
- **Structured Mode**: CloudEvent envelope serialized as a unified JSON document.
- Enterprise extensions (`ce-correlationid`, `ce-causationid`, `ce-tenantid`) flow automatically across asynchronous boundaries.

### 6. First-Class Distributed Observability
Observability is baked into the framework runtime, not bolted on via wrappers:
- **Distributed Tracing**: Native `System.Diagnostics.ActivitySource("Centra", "1.0.0")` with W3C TraceContext (`traceparent`, `tracestate`) injection and extraction.
- **Span Semantic Roles**: Correct `ActivityKind` assignment (`Producer` on publish, `Consumer` on subscribe, `Client` on RPC invoke, `Server` on handler, `Internal` on state/locks/actors/workflows).
- **Meters**: Native `System.Diagnostics.Metrics.Meter("Centra", "1.0.0")` reporting counters, operation duration histograms, and active gauges.
- **Structured Logging**: Compile-time source-generated `[LoggerMessage]` with zero boxed arguments.

---

## ⚡ Performance Comparison: In-Process vs Sidecar

| Metric | Sidecar Architecture (e.g. Dapr) | Centra (In-Process C#) |
| :--- | :--- | :--- |
| **Process Count** | 2 processes per pod/container | 1 process |
| **Network Hop** | App ──> localhost TCP ──> Sidecar | In-process native method call |
| **Loopback Latency** | ~1.5 ms – 4.0 ms | **0 ms (In-process)** |
| **Serialization Hops** | 2 hops (App -> JSON/gRPC -> Sidecar -> Driver) | **1 hop (App -> Driver)** |
| **Container Memory** | ~30 MB – 80 MB extra per container | **0 MB extra** |
| **Context Propagation** | HTTP headers over loopback | W3C ambient `Activity.Current` |
