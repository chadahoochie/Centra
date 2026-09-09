# Docker Compose Multi-Node Cluster

> Deploy a 3-replica production simulation stack with real Redis, RabbitMQ, Centra Control Plane, and a complete OpenTelemetry/Grafana observability pipeline.

---

## 🌟 Overview

The `samples/DockerStack` sample runs genuinely separated processes and containers to demonstrate production-grade distributed execution:
- **3 Service Nodes** (`node-1`, `node-2`, `node-3`) running `Centra.Sample.DockerStack`.
- **Centra Control Plane** tracking live cluster topology and heartbeats.
- **Redis 7** backing distributed locks (leases) and shared state (optimistic CAS).
- **RabbitMQ 4** backing enterprise pub/sub topics.
- **Traffic Simulator** generating continuous background traffic across all capabilities.
- **Full Observability Stack**: OpenTelemetry Collector -> Tempo (Traces), Loki (Logs), Prometheus (Metrics) -> Grafana.

---

## 🏗️ Topology & Port Mappings

| Service | Container / Port | Role |
| :--- | :--- | :--- |
| `control-plane` | `:5000` | Central component catalog, topology tracking, SSE sync |
| `node-1` | `:8081` | Cluster node replica 1 (AppId: `dockerstack-node`) |
| `node-2` | `:8082` | Cluster node replica 2 (AppId: `dockerstack-node`) |
| `node-3` | `:8083` | Cluster node replica 3 (AppId: `dockerstack-node`) |
| `redis` | `:6379` | Backs shared state and distributed lock store |
| `rabbitmq` | `:5672`, `:15672` | AMQP broker (Management UI on `:15672`, guest/guest) |
| `simulator` | Background worker | Drives automated traffic across nodes and endpoints |
| `otel-collector`| `:4317` (OTLP) | Fans out telemetry to Tempo, Prometheus, and Loki |
| `grafana` | `:3000` | Auto-provisioned dashboards (no login required) |

---

## 🏃 Running the Cluster

Start all containers in the background:

```bash
docker compose -f samples/DockerStack/docker-compose.yml up --build
```

Wait until all containers report healthy, then access Grafana or exercise the cluster with the curl recipes below.

To tear down the cluster:

```bash
docker compose -f samples/DockerStack/docker-compose.yml down -v
```

---

## 🧪 Interactive Demonstrations

All endpoints can be called against `:8081`, `:8082`, or `:8083` interchangeably.

### 1. Distributed Cron: Exactly-Once Execution
The same cron job runs on all 3 nodes, but Centra wraps each tick in a distributed Redis lock:

```bash
curl http://localhost:8081/cron/last-run
curl http://localhost:8082/cron/last-run
curl http://localhost:8083/cron/last-run
```
All three nodes report the exact same `HandledByInstanceId` and `Iteration`. The iteration advances once per period across the cluster, never three times.

### 2. Virtual Actors: Partition Ring & Transparent RPC
Invoke 20 virtual actor turns against `node-1`:

```bash
for i in $(seq 1 20); do curl -s -X POST "http://localhost:8081/actors/actor-$i/increment"; done
curl http://localhost:8082/actors/actor-1
curl http://localhost:8083/actors/actor-2
```
Each response reveals the owning node in `HandledByInstanceId`. The consistent hash ring distributes actors across `node-1`, `node-2`, and `node-3`. Querying an actor from a node that doesn't own it automatically proxies the call over HTTP to the owning node.

### 3. Service Invocation: Round-Robin RPC
```bash
curl http://localhost:8081/peers/info
curl http://localhost:8081/peers/info
curl http://localhost:8081/peers/info
```
The `InstanceId` in the response rotates across all three replicas as the Control Plane endpoint resolver round-robins requests.

### 4. RabbitMQ Pub/Sub: Cluster-Wide CloudEvents
```bash
curl -X POST http://localhost:8081/tasks/dispatch \
  -H "Content-Type: application/json" \
  -d '{"taskType":"demo","payload":"hello-world"}'

curl http://localhost:8082/tasks
curl http://localhost:8083/tasks
```
The CloudEvent published by `node-1` is consumed and recorded by the cluster consumers.

### 5. Redis State: Optimistic Concurrency (CAS) Retries
```bash
curl http://localhost:8081/state/counter
curl -X POST http://localhost:8082/state/counter/increment -H "Content-Type: application/json" -d '{}'
curl -X POST http://localhost:8083/state/counter/increment -H "Content-Type: application/json" -d '{"simulateConflict":true}'
```
`RetryAttempts` illustrates Centra's ETag-based CAS retry mechanism resolving race conditions transparently.

### 6. Distributed Locks: Leader Election
```bash
curl -X POST http://localhost:8081/leader/acquire  # 200 OK - node-1 acquires lease
curl -X POST http://localhost:8082/leader/acquire  # 409 Conflict - node-1 holds lease
curl -X POST http://localhost:8081/leader/release  # 200 OK - lease released
curl -X POST http://localhost:8082/leader/acquire  # 200 OK - node-2 now succeeds
```

### 7. Workflows & Sagas with Compensation
Start a saga via Centra's built-in workflow endpoints:

```bash
# Settle an order below $1000: Succeeds
curl -X POST http://localhost:8081/centra/workflows/OrderProcessingWorkflow/start \
  -H "Content-Type: application/json" \
  -d '{"orderId":"ord-1","customerId":"cust-1","productId":"sku-1","quantity":2,"totalAmount":250.00}'

# Trigger payment failure and saga compensation with amount > $1000:
curl -X POST http://localhost:8081/centra/workflows/OrderProcessingWorkflow/start \
  -H "Content-Type: application/json" \
  -d '{"orderId":"ord-2","customerId":"cust-1","productId":"sku-1","quantity":2,"totalAmount":1500.00}'
```

Query the instance history:
```bash
curl http://localhost:8082/centra/workflows/<instanceId>/history
```
Observe that when payment failed, the saga automatically triggered `ReleaseInventoryCompensationActivity` in reverse order.

---

## 📊 Live Observability Dashboards

Open Grafana in your browser (no password required):

```
http://localhost:3000
```

Navigate to the pre-provisioned dashboard: **Centra / Centra DockerStack: Baked-in Telemetry**.

Panels include:
- **Service Invocation Request Rate & p95 Latency**: Live RPC metrics.
- **RabbitMQ Pub/Sub Rate**: Messages published and consumed per second.
- **Redis State Operations & CAS Conflicts**: ETag retry rates under concurrent mutations.
- **Distributed Lock Acquisitions**: Mutual exclusion holding times.
- **Control Plane Heartbeats**: Health status of all cluster replicas.
- **Live Tempo Trace Panel**: Search traces filtered by `.centra.component` (`pubsub`, `state`, `lock`, `invoke`, `actors`, `workflows`).
- **Live Loki Log Panel**: Correlated structured logs with trace IDs.
