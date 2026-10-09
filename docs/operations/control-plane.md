# Centra Control Plane Architecture & REST API

> Centralized component management, topology tracking, secret resolution, real-time Server-Sent Events (SSE) synchronization, and runtime inspection.

---

## 🏛️ Role in the Architecture

> [!TIP]
> Not sure if your architecture requires the Control Plane? Consult the [Control Plane Decision Guide](when-to-use-control-plane.md) to compare standalone direct-driver mode with orchestrated cluster mode.

The **Centra Control Plane** (`Centra.ControlPlane`) serves as the central brain for a Centra cluster:
1. **Component Catalog**: Eliminates scattered YAML component files. Components (State Stores, Pub/Sub Brokers, Locks, Bindings) are configured centrally.
2. **Secret Resolution**: Resolves secret references (`secretKeyRef`) from environment variables or secret vaults before broadcasting to consumer nodes.
3. **Live SSE Streaming**: Pushes configuration changes, new component definitions, and resilience policy updates to running instances in real-time over persistent HTTP Server-Sent Events.
4. **Cluster Topology & Heartbeats**: Tracks active node replicas, their base URLs, and health statuses, powering client-side round-robin load balancing and consistent hash actor placement.
5. **Runtime Inspection**: Inspects active actor counts, passivates idle actors, and queries workflow instance states and event histories.

---

## 🌐 REST API Reference

All endpoints are hosted under `/api/v1`.

### 1. Component Catalog

#### `GET /api/v1/components`
Retrieves all registered components with secrets resolved.

#### `GET /api/v1/components/{name}`
Retrieves a specific component definition by name.

#### `POST /api/v1/components`
Registers or updates a component definition. Automatically broadcasts a `ComponentSyncEvent` to connected nodes.
```json
{
  "name": "statestore",
  "type": "StateStore",
  "version": "v1",
  "metadata": {
    "provider": "redis",
    "configuration": "localhost:6379"
  }
}
```

#### `DELETE /api/v1/components/{name}`
Deletes a component definition and notifies active nodes.

---

### 2. Resilience Policies

#### `GET /api/v1/resilience`
Lists all dynamic resilience policy definitions.

#### `POST /api/v1/resilience`
Upserts a resilience policy and broadcasts updates via SSE to all nodes.

#### `DELETE /api/v1/resilience/{name}`
Deletes a resilience policy.

---

### 3. Real-Time Streaming (SSE)

#### `GET /api/v1/sync/stream?appId={appId}&instanceId={instanceId}`
Opens a persistent Server-Sent Events stream (`text/event-stream`):
- On connection, sends a `FullSync` snapshot containing all active component definitions.
- As components are added, updated, or removed, emits live mutation events (`Added`, `Updated`, `Removed`).

#### `GET /api/v1/resilience/stream?appId={appId}&instanceId={instanceId}`
Opens a persistent SSE stream for dynamic Polly v8 resilience policies.

---

### 4. Topology & Heartbeats

#### `POST /api/v1/heartbeat`
Application nodes emit heartbeats periodically (default every 5 seconds):
```json
{
  "appId": "orders-service",
  "instanceId": "node-1",
  "clusterId": "cluster-alpha",
  "serviceAddress": "http://10.0.0.15:5000",
  "status": "Healthy",
  "metadata": {}
}
```

#### `GET /api/v1/topology?clusterId={clusterId}`
Returns active, non-expired service nodes. When `clusterId` is provided, filters strictly to nodes within that designated cluster partition.

---

### 5. High Availability & Diagnostics

#### `GET /api/v1/health`
Bypasses standby redirection to provide local replica status:
```json
{
  "status": "Healthy",
  "role": "Active",
  "isLeader": true
}
```
Standby replicas return:
- HTTP 200 with `role: "Standby"`, `isLeader: false`, and `leaderEndpoint: "http://cp-1:8080"`
- Response headers `X-Centra-Role: Standby` and `X-Centra-Leader: http://cp-1:8080`
- All other API endpoints on a standby replica return `307 Temporary Redirect` to the active leader.

---

### 6. Virtual Actors & Workflows Inspection

#### `GET /api/v1/actors/types`
Lists all registered actor type names across the cluster.

#### `GET /api/v1/actors/activations`
Returns the count of currently active in-memory actor instances.

#### `POST /api/v1/actors/{actorType}/{actorId}/passivate`
Manually passivates a specific actor instance, flushing state and releasing memory.

#### `GET /api/v1/workflows/definitions`
Returns metadata of all registered workflows.

#### `GET /api/v1/workflows/instances/{instanceId}`
Returns the current execution state, status (`Running`, `Completed`, `Failed`, `Suspended`), and failure details of a workflow.

#### `GET /api/v1/workflows/instances/{instanceId}/history?redact=true`
Returns the append-only event stream of past activities and timers for an orchestration instance.
When `redact=true` is requested (or `RedactWorkflowData` is configured server-side), payload and failure detail fields are redacted with `[REDACTED]` to enforce zero-PHI boundaries under HIPAA compliance.

---

### 7. Embedded Real-Time Dashboard

#### `GET /dashboard`
Serves a responsive single-page monitoring dashboard providing real-time views of:
- High-availability cluster role (Active Leader vs. Standby Replica).
- Multi-cluster node topology and status.
- Registered state stores, pub/sub brokers, and bindings.
- Active virtual actor types and instance activations.
- Workflow orchestration instances, states, and history.

---

## 🔗 Related Documentation

- [When to Use Control Plane (Decision Guide)](when-to-use-control-plane.md)
- [Control Plane Expansion & Cluster Formation Guide](../plans/control-plane-expansion-plan.md)
- [Angular Dashboard & Datadog Telemetry Architecture](../architecture/control-plane-dashboard-recommendations.md)
- [Configuration & Options Reference](configuration-reference.md)
- [Observability, Distributed Tracing & Metrics](observability.md)
