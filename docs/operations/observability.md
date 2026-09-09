# Observability, Distributed Tracing & Metrics

> Built-in OpenTelemetry instrumentation covering distributed tracing (`ActivitySource`), standardized metrics (`Meter`), and zero-allocation structured logging with no third-party sidecars required.

---

## 🌟 Observability Philosophy

Observability is a foundational pillar in Centra:
- **Zero Overhead when Inactive**: All activity starts and meter checks use `Source.HasListeners()` or `Meter.Create...()`. If no telemetry collector is attached, execution overhead is zero.
- **W3C Standards Compliant**: Trace context (`traceparent`, `tracestate`) is injected into all outbound CloudEvents and RPC calls, and extracted automatically on receipt.
- **Semantic Roles**: Every span uses the OpenTelemetry specification's `ActivityKind` to clearly distinguish producers, consumers, clients, servers, and internal operations.

---

## 🔍 Distributed Tracing (`ActivitySource`)

Centra instruments an `ActivitySource` with name **`Centra`** (version `1.0.0`):

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("Centra");
        tracing.AddSource("Centra.ControlPlane");
        tracing.AddOtlpExporter();
    });
```

### Span Semantic Conventions

| Subsystem | Span Name | ActivityKind | Tags Recorded |
| :--- | :--- | :--- | :--- |
| **Pub/Sub Publish** | `Centra.PubSub.Publish` | `Producer` | `centra.component = pubsub`, `centra.pubsub.name`, `messaging.destination = {topic}` |
| **Pub/Sub Process** | `Centra.PubSub.Process` | `Consumer` | `centra.component = pubsub`, `centra.pubsub.name`, `messaging.source = {topic}` |
| **RPC Invocation** | `Centra.Invoke.{method}` | `Client` | `centra.component = invoke`, `peer.service = {appId}`, `rpc.method` |
| **RPC Handler** | `Centra.Handle.{method}` | `Server` | `centra.component = invoke`, `rpc.system = centra`, `rpc.method` |
| **State Mutation** | `Centra.State.{operation}` | `Internal` | `centra.component = state`, `centra.store.name`, `centra.key`, `centra.operation` |
| **Distributed Lock** | `Centra.Lock.{operation}` | `Internal` | `centra.component = lock`, `centra.lock_store.name`, `centra.resource` |
| **Binding Trigger** | `Centra.Binding.Trigger` | `Consumer` | `centra.component = binding`, `centra.binding.name` |
| **Binding Invoke** | `Centra.Binding.Invoke` | `Producer` | `centra.component = binding`, `centra.binding.name`, `centra.binding.operation` |
| **Virtual Actors** | `Actor.Invoke` | `Client` | `centra.component = actors`, `actor.type`, `actor.id`, `actor.method` |
| **Workflows** | `Workflow.Run` / `Activity.Run` | `Internal` | `centra.component = workflows`, `workflow.name`, `activity.name` |

---

## 📊 Standard Metrics (`Meter`)

Centra instruments a `Meter` with name **`Centra`** (version `1.0.0`):

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Centra");
        metrics.AddMeter("Centra.ControlPlane");
        metrics.AddPrometheusExporter(); // or AddOtlpExporter()
    });
```

### Metrics Catalog

| Instrument Name | Type | Unit | Description |
| :--- | :--- | :--- | :--- |
| `centra.state.operations` | Counter | `ea` | Total count of state store operations (tags: `centra.store.name`, `centra.operation`, `status`) |
| `centra.state.operation.duration` | Histogram | `ms` | Duration of state store operations |
| `centra.pubsub.messages.published`| Counter | `ea` | Total messages published to topics |
| `centra.pubsub.publish.duration` | Histogram | `ms` | Duration to publish a message |
| `centra.pubsub.messages.consumed` | Counter | `ea` | Total messages consumed from topics |
| `centra.pubsub.process.duration` | Histogram | `ms` | Duration to process a message |
| `centra.invocation.requests` | Counter | `ea` | Total service invocation requests |
| `centra.invocation.duration` | Histogram | `ms` | Duration of service invocation RPCs |
| `centra.lock.acquisitions` | Counter | `ea` | Total distributed lock acquisitions |
| `centra.lock.hold.duration` | Histogram | `ms` | Duration a distributed lock lease was held |
| `centra.binding.invocations.total`| Counter | `ea` | Total output binding invocations |
| `centra.binding.triggers.total` | Counter | `ea` | Total input binding triggers handled |
| `centra.resilience.retries_total` | Counter | `ea` | Total resilience retry attempts |
| `centra.resilience.circuit_state_transitions_total` | Counter | `ea` | Total circuit breaker state transitions |
| `centra.resilience.timeouts_total`| Counter | `ea` | Total resilience timeout expirations |
| `centra.resilience.rejections_total` | Counter | `ea` | Total executions rejected by rate limiter or bulkhead |

---

## 📈 Grafana Dashboards

The `samples/DockerStack` sample provides pre-built dashboards that visualize all Centra metrics alongside Tempo traces and Loki logs. See [Docker Compose Cluster Guide](../getting-started/docker-cluster.md) to explore the live dashboard.
