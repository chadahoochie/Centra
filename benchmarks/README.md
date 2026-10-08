# Realistic RabbitMQ Benchmark Report: Centra vs. Package.Queueing

> Generated on **2026-10-08 02:53:13 UTC** against `amqp://rabbitmq:rabbitmq@127.0.0.1:38637/`.

## 📊 Executive Summary

This benchmark performs an apples-to-apples performance comparison between:
1. **Centra Framework (`Centra.Providers.RabbitMQ`)**: Native .NET 10 distributed application framework built on `RabbitMQ.Client 7.2.2`, CNCF CloudEvents v1.0 binary mode, zero-allocation memory pooling (`ReadOnlyMemory<byte>`, `ArrayPool<byte>.Shared`, `PooledDeliveryBody`), and pure `ValueTask` async execution.
2. **Package.Queueing (`VaxCare.Package.Queueing`)**: Production queueing library built on `RabbitMQ.Client 6.8.1`, `VaxCare.Pipelining` Steppers, synchronous channel leasing, and custom wire envelope framing.

---

## 📈 Performance Comparison Matrix

| Scenario | Queue Type | Payload | Conc | Centra Throughput | Package.Queueing Throughput | Speedup Multiplier | Centra P95 Latency | Pkg P95 Latency | Centra Alloc/Msg | Pkg Alloc/Msg | Alloc Reduction |
| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **Producer** | Classic | 1024 B | 4 | **50,012 msg/s** | 38,811 msg/s | **1.29x** | **0.03 ms** | 0.07 ms | **4,913 B** | 18,367 B | **73.3%** |
| **ConsumerDrain** | Classic | 1024 B | 4 | **35,388 msg/s** | 19,480 msg/s | **1.82x** | **0.00 ms** | 0.00 ms | **3,016 B** | 33,525 B | **91.0%** |
| **EndToEnd** | Classic | 1024 B | 4 | **27,501 msg/s** | 14,886 msg/s | **1.85x** | **185.65 ms** | 405.83 ms | **7,632 B** | 50,215 B | **84.8%** |
| **Producer** | Quorum | 1024 B | 4 | **48,759 msg/s** | 38,516 msg/s | **1.27x** | **0.02 ms** | 0.06 ms | **4,394 B** | 17,960 B | **75.5%** |
| **ConsumerDrain** | Quorum | 1024 B | 4 | **20,652 msg/s** | 12,961 msg/s | **1.59x** | **0.00 ms** | 0.00 ms | **2,992 B** | 33,511 B | **91.1%** |
| **EndToEnd** | Quorum | 1024 B | 4 | **16,238 msg/s** | 10,330 msg/s | **1.57x** | **397.93 ms** | 632.37 ms | **7,631 B** | 50,150 B | **84.8%** |

---

## 🏛️ Architectural Analysis & Drivers of Performance

### 1. Zero-Allocation Pipeline vs. Multi-Copy Boxing
- **Centra**: Operates with `ReadOnlyMemory<byte>` and pooled delivery buffers (`PooledDeliveryBody`). Deserialization decodes directly from memory buffers without allocating intermediate `byte[]` arrays. In CloudEvents binary mode, payload bytes pass directly to the socket.
- **Package.Queueing**: Calls `e.Body.ToArray()`, followed by LINQ `.Take(16).ToArray()` and `.Skip(16).ToArray()` in `DeserializeQueueMessage` to unpack the 16-byte Message GUID, allocating multiple arrays per message, wrapped in `Tuple<byte[], bool>` and `Stepper` delegates.

### 2. Modern Async Socket I/O vs. Synchronous Channel Locking
- **Centra**: Uses `RabbitMQ.Client 7.2.2`'s non-blocking `BasicPublishAsync` and `AsyncEventingBasicConsumer`. Sockets are serviced asynchronously without lock contention.
- **Package.Queueing**: Uses `RabbitMQ.Client 6.8.1` where channels require synchronization via `lock (leasedModel) { leasedModel.BasicPublish(...); }` and `lock (_channel) { _channel.BasicAck(...); }`. Under high concurrency, threads experience lock convoying.

### 3. Standards Compliance & Tracing Overhead
- **Centra**: Adheres to CNCF CloudEvents v1.0 and W3C TraceContext standards using `System.Diagnostics.ActivitySource`. Distributed context is propagated via `traceparent` headers with zero heap allocations when listeners are quiescent.
- **Package.Queueing**: Allocates custom `TelemetryContext` and `Activity` instances for every message dispatched through `Pipeline.ProcessQueueBytes`.
