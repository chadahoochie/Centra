# Centra Docker Compose Cluster Sample

A `docker compose up` stack that runs three replicas of `Centra.Sample.DockerStack` behind real
Redis, RabbitMQ, and a Centra Control Plane - demonstrating service invocation, Redis state,
RabbitMQ pub/sub, Redis distributed locks, single-execution cron bindings, cluster-aware virtual
actors, and a workflow saga, all running as genuinely separate processes/containers instead of the
in-memory single-process samples elsewhere in `samples/`.

The stack also ships a full observability path (OpenTelemetry Collector -> Tempo/Loki/Prometheus ->
Grafana) that showcases Centra's baked-in `ActivitySource`/`Meter` instrumentation
(`Centra.Diagnostics.CentraDiagnostics`/`CentraMeters` in `Centra.Core`, plus
`Centra.ControlPlane.Diagnostics.ControlPlaneMeters`) with zero bespoke tracing/metrics code in the
sample itself - see [Observability](#observability).

## Topology

| Service | Image / Build | Role |
|---|---|---|
| `redis` | `redis:7-alpine` | Backs the shared state store and distributed lock store |
| `rabbitmq` | `rabbitmq:4.3-management` | Backs pub/sub (management UI on `:15672`, guest/guest) |
| `control-plane` | `src/Centra.ControlPlane` | Tracks cluster topology/heartbeats, resolves peer addresses |
| `node-1` / `node-2` / `node-3` | `samples/Centra.Sample.DockerStack` | Three identical replicas of the cluster node, published on `:8081`/`:8082`/`:8083` |
| `simulator` | `samples/Centra.Sample.DockerStack.Simulator` | Background worker that steadily drives traffic at every demo below, so the stack keeps generating data without manual curls |
| `otel-collector` | `otel/opentelemetry-collector-contrib` | Receives OTLP traces/metrics/logs from every node and control-plane, fans them out |
| `tempo` | `grafana/tempo` | Trace storage/query backend |
| `loki` | `grafana/loki` | Log storage/query backend (receives logs natively via OTLP) |
| `prometheus` | `prom/prometheus` | Scrapes the collector's Prometheus exporter for metrics |
| `grafana` | `grafana/grafana` | Dashboards over Tempo/Loki/Prometheus, published on `:3000` |

All three nodes share the logical `AppId` `dockerstack-node` (so service invocation round-robins
across them) but each has a unique `InstanceId` equal to its compose `hostname` (`node-1`/`node-2`/`node-3`),
which is what the actor placement ring uses to spread actors across the cluster. See
`Centra.Sample.DockerStack/Program.cs` and `Centra.Sample.DockerStack/Cluster/` for how the two
identities are kept separate.

## Running

```sh
docker compose -f samples/DockerStack/docker-compose.yml up --build
```

Wait for all 6 containers to report healthy/running, then use the curl sequence below. Any
endpoint below can be called against `:8081`, `:8082`, or `:8083` interchangeably unless noted.

Tear down with:

```sh
docker compose -f samples/DockerStack/docker-compose.yml down -v
```

## Demonstrations

### Cron bindings: only one node executes each tick

The same job is registered identically on all three replicas; Centra's distributed job handler
wraps it with a shared Redis lock so only one replica wins each scheduled tick.

```sh
curl http://localhost:8081/cron/last-run
curl http://localhost:8082/cron/last-run
curl http://localhost:8083/cron/last-run
```

All three responses agree on the same `HandledByInstanceId` and `Iteration` - poll again after
10+ seconds and the iteration advances by roughly one, never by three.

### Actors: spread across nodes, transparently proxied

```sh
for i in $(seq 1 20); do curl -s -X POST "http://localhost:8081/actors/actor-$i/increment" | python3 -m json.tool; done
curl http://localhost:8082/actors/actor-1
curl http://localhost:8083/actors/actor-2
```

Every response includes `HandledByInstanceId` - across 20 actor ids you should see a mix of
`node-1`/`node-2`/`node-3`, and querying an actor from a node that doesn't own it still returns the
correct value (the request is proxied over HTTP to the owning peer).

### Service invocation: round-robin peer RPC

```sh
curl http://localhost:8081/peers/info
curl http://localhost:8081/peers/info
curl http://localhost:8081/peers/info
```

The `InstanceId` in the response rotates across the three nodes as Control Plane topology round-robins
the call, even though every request hit node-1.

### RabbitMQ pub/sub: delivered cluster-wide

```sh
curl -X POST http://localhost:8081/tasks/dispatch -H "Content-Type: application/json" \
  -d '{"taskType":"demo","payload":"hello"}'

curl http://localhost:8082/tasks
curl http://localhost:8083/tasks
```

The CloudEvent published by node-1 is consumed and recorded by whichever node(s) happen to receive
the RabbitMQ delivery.

### Redis state store: optimistic concurrency

```sh
curl http://localhost:8081/state/counter
curl -X POST http://localhost:8082/state/counter/increment -H "Content-Type: application/json" -d '{}'
curl -X POST http://localhost:8083/state/counter/increment -H "Content-Type: application/json" -d '{"simulateConflict":true}'
```

`RetryAttempts` in the response shows the ETag-based CAS retry loop kicking in when two nodes race
on the same key.

### Redis distributed lock: leader election

```sh
curl -X POST http://localhost:8081/leader/acquire
curl -X POST http://localhost:8082/leader/acquire   # 409 Conflict - node-1 already holds the lease
curl -X POST http://localhost:8081/leader/release
curl -X POST http://localhost:8082/leader/acquire   # now succeeds
```

### Workflows / sagas

Uses Centra's built-in workflow endpoints (`MapCentraWorkflowEndpoints`), no bespoke routes needed:

```sh
curl -X POST http://localhost:8081/centra/workflows/OrderProcessingWorkflow/start \
  -H "Content-Type: application/json" \
  -d '{"orderId":"ord-1","customerId":"cust-1","productId":"sku-1","quantity":2,"totalAmount":250.00}'

# use the returned instanceId
curl http://localhost:8082/centra/workflows/<instanceId>
```

Because workflow history lives in shared Redis state, you can start the saga on one node and poll
its status from any other. Submit a `totalAmount` over `1000.00` to see `ProcessPaymentActivity`
fail and the saga compensate the inventory reservation (`status` ends as `Failed`, and
`ReleaseInventoryCompensationActivity` ran).

## Simulator

The `simulator` container needs no manual interaction - it starts ~10 seconds after the nodes come
up and then loops forever, picking a random node and a weighted-random operation (actor increments,
counter increments with occasional simulated conflicts, task dispatches, peer-info/cron-status
queries, leader lease acquire/release cycles, and workflow starts - ~30% of which use a
`totalAmount` over `1000.00` to exercise the saga's compensation path). It's what keeps the Grafana
dashboards live without running the `curl` sequences below by hand; the sequences are still useful
for triggering a specific scenario on demand or for understanding what the simulator is doing.

Tune its behavior via environment variables in `docker-compose.yml` (`Simulation__NodeBaseUrls`,
`Simulation__IntervalMs`, `Simulation__ActorPoolSize`) - e.g. lower `Simulation__IntervalMs` for
denser traffic, or set `Simulation__ActorPoolSize` higher to spread actor placement across more IDs.

## Observability

Every node and the control-plane call `AddOpenTelemetry()` in `Program.cs` and export over OTLP to
`otel-collector:4317` (no other code changes - Centra's `ActivitySource("Centra")` and
`Meter("Centra")` / `Meter("Centra.ControlPlane")` are already instrumented deep inside
`Centra.Core`/`Centra.ControlPlane` for pub/sub, state, locks, invocation, bindings, and control-plane
sync/heartbeats). The collector fans traces out to Tempo, metrics to Prometheus, and logs to Loki.

Open Grafana (anonymous admin access, no login needed) once the stack is up:

```
http://localhost:3000
```

The **Centra / Centra DockerStack: Baked-in Telemetry** dashboard (auto-provisioned) shows:

- Service invocation request rate and p95 duration (round-robin RPC across nodes)
- RabbitMQ pub/sub publish/consume throughput
- Redis state store operation rate (including the ETag CAS retries from the counter demo)
- Redis distributed lock acquisitions (leader election)
- Control-plane heartbeats received per node
- A live Tempo trace search panel and a Loki log panel

Drive traffic first so the dashboard has data - run a few of the `curl` sequences above (actors,
service invocation, pub/sub, state, locks, workflows), then watch the panels update every 5s. Every
span exported to Tempo is tagged with `centra.component` (`pubsub`, `state`, `lock`, `invoke`,
`binding`, `actors`), so Explore -> Tempo -> TraceQL `{ .centra.component = "invoke" }` (for example)
isolates a single subsystem across all three nodes.

Tear down the observability containers along with everything else via `docker compose down -v`
(Tempo/Loki/Prometheus data is not persisted across restarts by design, to keep the sample stateless).
