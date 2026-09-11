# Centra Framework Documentation

> High-performance, cloud-native distributed application framework for .NET 10 engineered natively in C# with zero sidecars, modular abstractions, CNCF CloudEvents v1.0, Polly Core v8 resilience, distributed virtual actors, and deterministic workflows & sagas.

Welcome to the Centra documentation! This documentation suite is organized following the **Divio Documentation System**, separating tutorials, conceptual architectures, how-to guides, and detailed API references.

---

## 🗺️ Documentation Map

```
docs/
├── getting-started/          # Step-by-step tutorials to get up and running fast
├── architecture/             # Conceptual designs, invariants, and core principles
├── building-blocks/          # Developer guides and API references for each concern
├── providers/                # Production distributed provider configuration & drivers
└── operations/               # Control plane, observability, metrics, and options
```

---

## 🚀 1. Getting Started

| Guide | Description |
| :--- | :--- |
| [**Quickstart: First Microservice**](getting-started/quickstart.md) | Build a complete distributed microservice with state, pub/sub, locks, and RPC in under 5 minutes. |
| [**.NET Aspire Orchestration**](getting-started/aspire.md) | Run multi-replica clusters with live dashboards, service discovery, and health checks via .NET Aspire. |
| [**Docker Compose Cluster**](getting-started/docker-cluster.md) | Launch a 3-replica production simulation with Redis, RabbitMQ, Control Plane, OTel, and Grafana. |

---

## 🏛️ 2. Architecture & Concepts

| Guide | Description |
| :--- | :--- |
| [**Architectural Overview**](architecture/overview.md) | The six core pillars, zero-sidecar in-process execution, and zero-allocation memory conventions. |
| [**Package Ecosystem**](architecture/package-ecosystem.md) | Map of the 11 modular abstractions, 12 implementations, 7 providers, hosting, and control plane. |
| [**Concurrency & State Invariants**](architecture/concurrency-and-state.md) | Actor mailbox turns, dirty tracking, ETag CAS state commits, and distributed lock lease renewals. |
| [**Workflows & Sagas Engine**](architecture/workflows-and-sagas.md) | Deterministic event-sourced replay, durable timers, and automated LIFO saga compensations. |

---

## 🧱 3. Building Blocks & Developer Guides

| Guide | Description | Key Interfaces |
| :--- | :--- | :--- |
| [**State Management**](building-blocks/state-management.md) | Key/value persistence, optimistic concurrency (ETags), TTL, and transactional batches. | `IStateStore<T>`, `IStateStore`, `ITransactionalStateStore` |
| [**Pub/Sub & CloudEvents**](building-blocks/pubsub.md) | At-least-once messaging wrapped in CNCF CloudEvents v1.0, consumer groups, and dead lettering. | `IPubSubClient`, `IEventHandler<T>`, `[Topic]` |
| [**Distributed Locks**](building-blocks/distributed-locks.md) | Mutex coordination, lease heartbeats, and leader election across cluster replicas. | `IDistributedLockProvider`, `IDistributedLock` |
| [**Service Invocation**](building-blocks/service-invocation.md) | Strongly typed client RPC proxies, discovery, and client-side round-robin load balancing. | `IServiceInvoker`, `[ServiceClient]`, `[ServiceMethod]` |
| [**Schedulers & Bindings**](building-blocks/schedulers-and-bindings.md) | Bitmask cron scheduler, distributed single-execution jobs, inbound webhooks, and output bindings. | `IScheduler`, `[CronBinding]`, `IBindingTriggerHandler`, `IOutputBinding` |
| [**Virtual Actors**](building-blocks/virtual-actors.md) | High-throughput virtual actors with turn-based FIFO mailboxes, ETag state, and durable reminders. | `IActor`, `Actor`, `IActorProxyFactory`, `IActorStateManager`, `IRemindable` |
| [**Workflows & Sagas**](building-blocks/workflows-and-sagas.md) | Durable orchestrations, deterministic replay, durable timers, sagas, and CloudEvent event awaits. | `Workflow<TIn, TOut>`, `WorkflowActivity<TIn, TOut>`, `IWorkflowClient` |
| [**Distributed Resilience**](building-blocks/resilience.md) | Polly Core v8 pipelines (Timeout, Bulkhead, RateLimiter, CircuitBreaker, Retry) with SSE hot-reload. | `IResiliencePipelineProvider`, `IResiliencePolicyRegistry` |

---

## 🔌 4. Production Distributed Providers

Centra provides high-performance, native C# driver implementations for leading production infrastructure:

| Provider | Supported Capabilities | Driver Package |
| :--- | :--- | :--- |
| [**In-Memory**](providers/in-memory.md) | State Store, Pub/Sub, Distributed Locks, Output Bindings | `Centra.Providers.InMemory` |
| [**Redis**](providers/redis.md) | State Store (Lua CAS), Pub/Sub (CloudEvents binary), Distributed Locks (Lease renewal) | `Centra.Providers.Redis` |
| [**PostgreSQL**](providers/postgresql.md) | State Store (JSONB, ACID transactions, ETags, TTL), Distributed Locks (Lease table) | `Centra.Providers.PostgreSql` |
| [**RabbitMQ**](providers/rabbitmq.md) | Pub/Sub (AMQP topic exchange, CloudEvents headers, consumer groups, DLX) | `Centra.Providers.RabbitMQ` |
| [**SQL Server**](providers/sql-server.md) | State Store (Atomic MERGE, ETags, Tx, TTL), Distributed Locks (Lease table) | `Centra.Providers.SqlServer` |
| [**Azure Service Bus**](providers/azure-service-bus.md) | Pub/Sub (Cloud-native topics, subscriptions, CloudEvents application properties, DLQ) | `Centra.Providers.AzureServiceBus` |
| [**Azure Cosmos DB**](providers/cosmosdb.md) | State Store (Point reads, TransactionalBatch, ETags, TTL), Distributed Locks | `Centra.Providers.CosmosDb` |

---

## ⚙️ 5. Operations & Observability

| Guide | Description |
| :--- | :--- |
| [**Centra Control Plane**](operations/control-plane.md) | Centralized component catalog, secret resolution, topology tracking, SSE sync, and REST API. |
| [**When to Use Control Plane**](operations/when-to-use-control-plane.md) | Architectural decision guide comparing standalone direct-driver mode vs. orchestrated Control Plane mode. |
| [**Observability & Telemetry**](operations/observability.md) | OpenTelemetry distributed tracing (`ActivitySource`), metrics (`Meter`), and logging conventions. |
| [**Configuration & Options Reference**](operations/configuration-reference.md) | Declarative attributes vs. service collection extensions, options classes, environment variable bindings, and defaults. |
| [**NuGet Packaging & OIDC Publishing**](operations/nuget-publishing.md) | Automated packaging, NuGet.org Trusted Publishing (OIDC), and CI/CD release operations. |

---

## 🧪 Interactive Simulations & Samples

The repository includes runnable interactive simulations illustrating every capability:

```bash
# 1. Multi-instance cluster simulation (Leader election, shared state CAS, CloudEvents)
dotnet run --project samples/Centra.Sample.MultiInstance -- --demo

# 2. Virtual actors simulation (Turn-based concurrency, state persistence, durable reminders)
dotnet run --project samples/Centra.Sample.Actors -- --demo

# 3. Workflows & sagas simulation (Deterministic replay, LIFO compensations, durable timers)
dotnet run --project samples/Centra.Sample.Workflows -- --demo

# 4. Resilience & chaos simulation (Retries, circuit breaker trip/recovery, dynamic hot-reload)
dotnet run --project samples/Centra.Sample.Resilience -- --demo

# 5. Distributed bindings & schedulers simulation (Distributed cron, webhooks, output bindings)
dotnet run --project samples/Centra.Sample.Bindings -- --demo

# 6. Aspire multi-replica cloud orchestrator (Web dashboard, tracing, metrics)
dotnet run --project samples/Centra.AppHost

# 7. Full Docker Compose 3-node cluster with Redis, RabbitMQ, Control Plane, OTel, Grafana
docker compose -f samples/DockerStack/docker-compose.yml up --build
```
