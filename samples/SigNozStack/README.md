# Centra SigNoz Telemetry Showcase: Kitchen Sink Docker Compose Sample

A self-contained `docker compose up` stack running three replicas of `Centra.Sample.DockerStack` alongside real Redis, RabbitMQ, PostgreSQL, a Centra Control Plane, a background traffic simulator, and an integrated **SigNoz** observability engine (ClickHouse + ZooKeeper + OTel Collector + Query Service + Frontend).

This sample provides a complete "kitchen sink" demonstration of the Centra Distributed Application Framework with **fixed, deterministic ports** (avoiding ephemeral port changes encountered with Aspire) so every simulation scenario can be executed directly via copy-and-paste.

---

## 🏛️ Architecture & Port Mapping

| Service | Port(s) | Role |
|---|---|---|
| `signoz-frontend` | `:3301` | SigNoz Web UI, Metrics Dashboards & Distributed Tracing Explorer |
| `control-plane` | `:8080` | Centra Control Plane (Service discovery, heartbeat tracking, topology sync) |
| `node-1` | `:8081` | Cluster node replica 1 (`Centra.Sample.DockerStack`) |
| `node-2` | `:8082` | Cluster node replica 2 (`Centra.Sample.DockerStack`) |
| `node-3` | `:8083` | Cluster node replica 3 (`Centra.Sample.DockerStack`) |
| `simulator` | *internal* | Autonomous traffic generator running continuous scenario loops |
| `signoz-otel-collector` | `:4317` / `:4318` | OpenTelemetry Collector receiving OTLP gRPC & HTTP, writing to ClickHouse |
| `clickhouse` | `:9000` / `:8123` | High-performance columnar database backing SigNoz telemetry |
| `rabbitmq` | `:5672` / `:15672` | AMQP broker backing CNCF CloudEvents v1.0 pub/sub (Management UI: `guest`/`guest`) |
| `redis` | `:6379` | Shared Redis cache backing state store and distributed locks |
| `postgres` | `:5432` | PostgreSQL 17 backing relational state and lock stores (`centra`/`centra_password`) |

All three cluster replicas share the logical `AppId` `dockerstack-node` (so service invocation round-robins across them) while maintaining unique instance IDs (`node-1`, `node-2`, `node-3`) for consistent hash ring actor placement.

---

## 🚀 Quick Start

### 1. Start the Stack

```bash
docker compose -f samples/SigNozStack/docker-compose.yml up --build -d
```

Wait ~15-20 seconds for all containers to initialize and report healthy.

### 2. Open SigNoz UI

Navigate to:
```
http://localhost:3301
```

When opening SigNoz for the first time, complete the initial admin account setup if prompted.
To load the dashboard:
Navigate to **Dashboards** > click **+ New Dashboard** > **Import JSON** > upload or paste `signoz/dashboards/centra-kitchen-sink.json`.
The 12 panels will immediately render the live metrics streaming from the cluster replicas.

### 3. Run the Automated Simulation

You can run the full burst simulation with one command:
```bash
./samples/SigNozStack/simulate.sh
```

Or copy-and-paste any of the interactive walkthrough commands below.

---

## 🧪 Interactive Walkthrough (100% Copy-and-Paste)

Every endpoint below can be called against `:8081`, `:8082`, or `:8083` interchangeably.

### 1. Service Invocation: Round-Robin RPC across Replicas
Centra's `IServiceInvoker` resolves peers dynamically from the Control Plane topology and load-balances calls:

```bash
curl http://localhost:8081/peers/info
curl http://localhost:8081/peers/info
curl http://localhost:8081/peers/info
```
*Observe the `instanceId` rotating between `node-1`, `node-2`, and `node-3` even though all requests hit `:8081`.*

### 2. Virtual Actors: Consistent Hash Ring Placement & Transparent Proxying
Invoke 20 actor turns across the cluster:

```bash
for i in $(seq 1 20); do curl -s -X POST "http://localhost:8081/actors/actor-$i/increment"; done
curl http://localhost:8082/actors/actor-1
curl http://localhost:8083/actors/actor-2
```
*Each actor is assigned to a specific node by the hash ring. Querying an actor from a non-owning node automatically proxies the call over HTTP to the owning replica.*

### 3. Distributed Workflows & Sagas: Happy Path and Compensation
Start a successful order saga:

```bash
curl -X POST http://localhost:8081/centra/workflows/OrderProcessingWorkflow/start \
  -H "Content-Type: application/json" \
  -d '{"orderId":"ord-success-1","customerId":"cust-1","productId":"sku-100","quantity":2,"totalAmount":250.00}'
```

Start a failing order saga that exceeds the $1,000 threshold, triggering compensation:

```bash
curl -X POST http://localhost:8082/centra/workflows/OrderProcessingWorkflow/start \
  -H "Content-Type: application/json" \
  -d '{"orderId":"ord-fail-1","customerId":"cust-2","productId":"sku-200","quantity":5,"totalAmount":1500.00}'
```

Poll workflow status from any node:
```bash
curl http://localhost:8083/centra/workflows/<instanceId>
```
*The failing saga shows `ProcessPaymentActivity` failing and `ReleaseInventoryCompensationActivity` executing in reverse order.*

### 4. RabbitMQ Pub/Sub: CNCF CloudEvents v1.0
Publish a CloudEvent from `node-1`:

```bash
curl -X POST http://localhost:8081/tasks/dispatch \
  -H "Content-Type: application/json" \
  -d '{"taskType":"telemetryDemo","payload":"Dispatched via CloudEvents binary mode"}'
```

Inspect processed tasks on peer replicas:
```bash
curl http://localhost:8082/tasks
curl http://localhost:8083/tasks
```

### 5. Redis State Store: Optimistic Concurrency & ETag CAS Retries
Read current counter:
```bash
curl http://localhost:8081/state/counter
```

Simulate concurrent contention triggering the ETag CAS retry loop:
```bash
curl -X POST http://localhost:8082/state/counter/increment \
  -H "Content-Type: application/json" \
  -d '{"incrementBy":1,"simulateConflict":true}'
```
*`RetryAttempts` in the response reveals the optimistic concurrency CAS retry loop in action.*

### 6. Distributed Mutual Exclusion: Leader Election Locks
`node-1` acquires a 15-second leadership lease:
```bash
curl -X POST http://localhost:8081/leader/acquire
```

`node-2` attempts to acquire while held (returns `409 Conflict`):
```bash
curl -X POST http://localhost:8082/leader/acquire
```

`node-1` releases leadership, allowing `node-2` to acquire:
```bash
curl -X POST http://localhost:8081/leader/release
curl -X POST http://localhost:8082/leader/acquire
curl -X POST http://localhost:8082/leader/release
```

### 7. Distributed Cron Bindings: Single Execution Across Replicas
A cron binding is registered identically on all 3 nodes. Centra wraps each tick in a shared distributed lock:

```bash
curl http://localhost:8081/cron/last-run
curl http://localhost:8082/cron/last-run
curl http://localhost:8083/cron/last-run
```
*All replicas report the same `HandledByInstanceId` and advancing `Iteration`, proving only 1 replica executes per scheduled tick.*

### 8. Relational Persistence (PostgreSQL State & Locks)
Write state to PostgreSQL from `node-1` and verify cross-node read from `node-2`:
```bash
curl -X POST http://localhost:8081/db/postgres/order-999 \
  -H "Content-Type: application/json" \
  -d '{"value":"{\"status\":\"ApprovedPG\",\"total\":450}"}'
curl http://localhost:8082/db/postgres/order-999
```

Update the state from `node-2` and read updated state and ETag from `node-3`:
```bash
curl -X POST http://localhost:8082/db/postgres/order-999 \
  -H "Content-Type: application/json" \
  -d '{"value":"{\"status\":\"CompletedPG\",\"total\":450}"}'
curl http://localhost:8083/db/postgres/order-999
```

### 9. Polly v8 Resilience Pipeline & Fault Tolerance
Trigger a transient failure to observe automatic retries:
```bash
curl -X POST "http://localhost:8081/resilience/simulate?induceFailure=true"
```
*Response shows `Attempts: 3` as Polly's retry policy successfully retries and recovers.*

---

## 📊 SigNoz Observability Guide

Open **SigNoz** at `http://localhost:3301`:

### Pre-Provisioned Dashboard
Go to **Dashboards** > **Centra Distributed Framework - Kitchen Sink Overview**:
- **Service Invocation Request Rate & P95 Latency**: Live RPC throughput across cluster replicas.
- **RabbitMQ Pub/Sub Throughput**: Messages published and consumed per second.
- **State Store Operations Rate & P95 Latency**: Redis and PostgreSQL transactions.
- **Distributed Lock Acquisitions**: Successes vs lock conflicts (409).
- **Distributed Cron Executions**: Periodic ticks showing cluster mutual exclusion.
- **Control Plane Heartbeats**: Heartbeat health reporting per replica.
- **Polly Resilience Retries**: Handled retries and circuit transitions.

### Distributed Traces Explorer
Go to **Traces** > filter by `serviceName = dockerstack-node`:
- Filter by `centra.component = pubsub` to view CloudEvent publishing, transport over RabbitMQ, and consumer invocation with full context propagation (`traceparent`).
- Filter by `centra.component = invoke` to view client-to-server RPC spans across replicas.
- Search for `OrderProcessingWorkflow` to inspect the full distributed saga activity tree and compensation spans.
- Filter by `centra.component = actors` to see actor mailbox turns and proxy hops.

---

## 🧹 Teardown

To shut down and clean up all containers and volumes:

```bash
docker compose -f samples/SigNozStack/docker-compose.yml down -v
```
