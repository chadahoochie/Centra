# Centra Multi-Instance Cluster Sample

A multi-replica sample application demonstrating **native service discovery**, **distributed mutual exclusion (leader election)**, **shared state with optimistic concurrency (ETags)**, **CNCF CloudEvents pub/sub**, and **cluster topology tracking** across independent service instances.

---

## 🏛️ Architecture & Capabilities

```mermaid
flowchart TD
    CP[Centra Control Plane<br/>Topology & Heartbeats]
    
    subgraph Cluster [Multi-Instance Cluster (AppId: multi-instance-service)]
        Node1[Node 1<br/>:5101]
        Node2[Node 2<br/>:5102]
        Node3[Node 3<br/>:5103]
    end
    
    SharedState[(Shared State Store<br/>Redis / In-Memory)]
    Broker[(Message Broker<br/>Pub/Sub)]
    
    Node1 <-->|Heartbeats| CP
    Node2 <-->|Heartbeats| CP
    Node3 <-->|Heartbeats| CP
    
    Node1 -.->|Round-Robin RPC| Node2
    Node2 -.->|Round-Robin RPC| Node3
    
    Node1 <--> SharedState
    Node2 <--> SharedState
    Node3 <--> SharedState
    
    Node1 <--> Broker
    Node2 <--> Broker
    Node3 <--> Broker
```

### Key Demonstrations

1. **Native Service Discovery & Client-Side Load Balancing**:
   - `INodePeerClient` decorated with `[ServiceClient("multi-instance-service")]`.
   - Replicas query peer endpoints via `ControlPlaneServiceEndpointResolver`, automatically load balancing round-robin across healthy nodes with zero external service meshes or sidecars.
2. **Leader Election & Mutex Locking**:
   - Multiple instances compete for a shared distributed lock resource (`cluster-leader`) via `IDistributedLockProvider`.
   - Only one active leader at a time; secondary nodes yield cleanly and take over on lease expiration.
3. **Shared State & Optimistic Concurrency Control**:
   - Concurrent increments to shared keys via `IStateStore<T>`.
   - Automatic ETag collision detection and retries (`TrySetAsync`).
4. **CNCF CloudEvents Pub/Sub**:
   - Instances publish and consume domain events (`cluster.tasks`) across the cluster with ambient W3C tracecontext propagation.
5. **Cluster Topology Tracking**:
   - Each replica registers its `InstanceId`, service address metadata, and heartbeats with the Control Plane (`/api/v1/topology`).

---

## 🚀 Running the Sample

### Option 1: Automated In-Process 3-Node Simulation

Run the built-in automated simulation that boots 3 nodes in-process, elects a leader, exercises concurrent state mutations with ETag conflicts, and verifies CloudEvents pub/sub:

```bash
dotnet run --project samples/Centra.Sample.MultiInstance -- --demo
```

### Option 2: Standalone Cluster Nodes

Run multiple nodes independently on different ports:

```bash
# Terminal 1 - Node 1
dotnet run --project samples/Centra.Sample.MultiInstance -- --instance-id node-1 --urls "http://localhost:5101"

# Terminal 2 - Node 2
dotnet run --project samples/Centra.Sample.MultiInstance -- --instance-id node-2 --urls "http://localhost:5102"

# Terminal 3 - Node 3
dotnet run --project samples/Centra.Sample.MultiInstance -- --instance-id node-3 --urls "http://localhost:5103"
```

### Option 3: Orchestrated via .NET Aspire

Run with .NET Aspire AppHost which automatically spawns 3 replicas and connects them to the Centra Control Plane:

```bash
dotnet run --project samples/Centra.AppHost
```

---

## 🧪 Testing the Endpoints

Once nodes are running, interact with any node:

```bash
# 1. Query node identity
curl http://localhost:5101/instance

# 2. Call peer RPC (round-robins across nodes via service discovery)
curl http://localhost:5101/peers/info
curl http://localhost:5101/peers/info

# 3. Compete for cluster leadership
curl -X POST http://localhost:5101/leader/acquire
curl -X POST http://localhost:5102/leader/acquire  # Returns 409 Conflict

# 4. Mutate shared state
curl -X POST http://localhost:5101/state/counter/increment
curl http://localhost:5102/state/counter

# 5. Dispatch cluster task (CloudEvents pub/sub)
curl -X POST http://localhost:5101/tasks/dispatch \
  -H "Content-Type: application/json" \
  -d '{"taskType":"compute","payload":"data-42"}'
```

---

## 🔗 Related Documentation

- [When to Use Control Plane (Decision Guide)](../../docs/operations/when-to-use-control-plane.md)
- [.NET Aspire Cloud-Native Orchestration](../../docs/getting-started/aspire.md)
- [Distributed Mutual Exclusion & Locks](../../docs/building-blocks/distributed-locks.md)
- [State Management & Optimistic Concurrency](../../docs/building-blocks/state-management.md)
