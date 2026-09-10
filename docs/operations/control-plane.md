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
  "serviceAddress": "http://10.0.0.15:5000",
  "status": "Healthy",
  "metadata": {}
}
```

#### `GET /api/v1/topology`
Returns all active, non-expired service nodes in the cluster.

---

### 5. Virtual Actors & Workflows Inspection

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

#### `GET /api/v1/workflows/instances/{instanceId}/history`
Returns the append-only event stream of past activities and timers for an orchestration instance.

---

## 🔗 Related Documentation

- [When to Use Control Plane (Decision Guide)](when-to-use-control-plane.md)
- [Configuration & Options Reference](configuration-reference.md)
- [Observability, Distributed Tracing & Metrics](observability.md)
