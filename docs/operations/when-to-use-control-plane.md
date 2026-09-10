# When to Use the Centra Control Plane (Decision Guide)

> Understand when to deploy the Centra Control Plane and when to run Centra microservices standalone using direct provider drivers and infrastructure-native service discovery.

---

## 🌟 Architecture & Core Philosophy

One of the defining pillars of Centra is **Modular Abstractions with Zero Dependency Drag** following the **Interface Segregation Principle (ISP)**.

Unlike legacy distributed runtimes that force an omnipresent sidecar or mandatory daemon into every deployment, Centra gives you two supported operating models:

1. **Standalone Mode (Without Control Plane)**: Microservices directly register provider drivers (RabbitMQ, Redis, PostgreSQL, SQL Server, Cosmos DB, Azure Service Bus) and resolve peer services via standard infrastructure mechanisms (DNS, Kubernetes Services, .NET Aspire, or configuration).
2. **Orchestrated Mode (With Control Plane)**: A dedicated `Centra.ControlPlane` service maintains a centralized component catalog, resolves secrets, broadcasts real-time Server-Sent Events (SSE) updates, and tracks dynamic cluster topology for actor placement and client-side load balancing.

```mermaid
flowchart TD
    subgraph StandaloneMode [Standalone Mode (Zero Control Plane)]
        App1[Producer Service] -->|Direct AMQP| RMQ[(RabbitMQ Broker)]
        App2[Consumer Service] -->|Direct AMQP| RMQ
        App2 -->|Static Config / K8s DNS / Aspire| App3[API Target Service]
    end

    subgraph OrchestratedMode [Orchestrated Mode (With Control Plane)]
        CP[Centra Control Plane<br/>Catalog, Secrets, Topology]
        CP -.->|SSE Live Updates| Node1[Cluster Node 1]
        CP -.->|SSE Live Updates| Node2[Cluster Node 2]
        Node1 <-->|Heartbeats / Cluster Topology| CP
        Node2 <-->|Heartbeats / Cluster Topology| CP
    end
```

---

## 📊 At-a-Glance Decision Matrix

| Concern / Capability | Standalone Mode (No Control Plane) | Orchestrated Mode (With Control Plane) |
| :--- | :--- | :--- |
| **Component Configuration** | Defined in code, `appsettings.json`, or environment variables. | Centralized catalog stored in Control Plane; fetched at boot. |
| **Configuration Updates** | Requires pod restart or ASP.NET Core `IOptionsSnapshot`. | Hot-reloaded in real-time via Server-Sent Events (SSE) streams. |
| **Secrets Resolution** | Kubernetes Secrets, Azure Key Vault, or local env vars. | Central vault references (`secretKeyRef`) resolved centrally. |
| **Pub/Sub Messaging** | Direct broker connection via driver (RabbitMQ, Redis, ASB). | Direct broker connection (configured centrally or locally). |
| **State Stores & Locks** | Direct provider drivers (Redis, Postgres, SQL Server, etc.). | Direct provider drivers (configured centrally or locally). |
| **Service Invocation** | Resolved via [`ConfigurationServiceEndpointResolver`](../../src/Centra.Hosting/Invocation/ConfigurationServiceEndpointResolver.cs), K8s DNS, or Aspire. | Client-side round-robin load balancing via live cluster topology. |
| **Virtual Actor Placement** | Single-node / in-memory or fixed partition assignment. | Dynamic consistent-hash ring over live active node topology. |
| **Resilience Policies** | Static Polly Core v8 pipelines declared in application code. | Dynamically mutable Polly v8 pipelines pushed via live SSE. |
| **Operational Inspection** | Local `/health` endpoints and OpenTelemetry metrics/traces. | Central REST API for active actor passivation and workflow history. |

---

## ❌ When Control Plane is NOT Needed

You do **not** need the Control Plane if your application fits any of the following scenarios:

### 1. Direct Pub/Sub & Point-to-Point Messaging
When services interact primarily through message brokers like RabbitMQ or Azure Service Bus (as demonstrated in the [RabbitSimulation](../../samples/RabbitSimulation/README.md) sample):
- The producer and consumer connect directly to the broker.
- CloudEvents v1.0 packing, unpacking, and W3C `traceparent` context propagation happen in-process.
- No central catalog is required.

### 2. Standard Kubernetes Deployments
Kubernetes already provides robust primitives for concerns the Control Plane would otherwise address:
- **Service Discovery**: Kubernetes CoreDNS and `ClusterIP` services route HTTP RPC invocations reliably.
- **Secrets Management**: Native Kubernetes `Secret` mounts or CSI Secrets Store drivers handle credentials.
- **Scaling**: Horizontal Pod Autoscaler (HPA) manages replica counts without application-layer heartbeats.

### 3. .NET Aspire Local Orchestration
When orchestrating services locally with .NET Aspire:
- Aspire manages container lifecycles (e.g. RabbitMQ, Redis, Postgres).
- Connection strings and endpoints are automatically injected via `.WithReference(...)`.
- Centra's [`ConfigurationServiceEndpointResolver`](../../src/Centra.Hosting/Invocation/ConfigurationServiceEndpointResolver.cs) resolves target services directly using Aspire's `services:{appId}:http:0` conventions.

### 4. GitOps & Immutable Infrastructure
If your organization deploys using GitOps (ArgoCD, Flux) where configuration changes are deployed via CI/CD pipelines and blue/green rollouts, you do not need live SSE component streaming. Configuration can be managed via standard ASP.NET Core `IConfiguration` sources.

### 5. Single-Node or Minimal Footprint Workloads
For lightweight utilities, edge gateways, background workers, or standalone APIs, eliminating the Control Plane avoids operating an additional container or service process.

---

### Code Example: Running Standalone Without Control Plane

To run without Control Plane, configure your provider drivers and service discovery using standard .NET extension methods:

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Core Centra Registration (PubSub + Invocation only)
builder.Services.AddCentraPubSub();
builder.Services.AddCentraInvocation();

// 2. Direct RabbitMQ Pub/Sub Driver
builder.Services.AddCentraRabbitMQPubSub("pubsub", options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("rabbitmq") 
        ?? "amqp://guest:guest@localhost:5672";
});

// 3. Register Topic Handler
builder.Services.AddCentraEventHandler<OrderSubmittedEventHandler, OrderMessage>("pubsub", "orders.new");

// 4. Register Typed RPC Client (Resolved via Configuration/Aspire/DNS)
builder.Services.AddCentraServiceClient<IOrderApiClient>();

var app = builder.Build();
await app.RunAsync();
```

In `appsettings.json` (or environment variables), provide the target service address directly:

```json
{
  "Centra": {
    "Services": {
      "rabbit-api": {
        "Address": "http://localhost:5200"
      }
    }
  }
}
```

Centra's [`ConfigurationServiceEndpointResolver`](../../src/Centra.Hosting/Invocation/ConfigurationServiceEndpointResolver.cs) automatically reads `Centra:Services:{serviceId}:Address` or Aspire's `services:{serviceId}:http:0`, providing direct RPC connectivity with zero Control Plane dependencies.

---

## ✅ When Control Plane IS Needed

Deploy the **Centra Control Plane** (`Centra.ControlPlane`) when your architecture requires centralized coordination, dynamic clustering, or live runtime governance:

### 1. Dynamic Multi-Node Actor Clusters
When using **Centra Virtual Actors** (`Centra.Actors`) across multiple shifting replicas:
- Actors must be placed deterministically across cluster nodes using consistent hashing.
- Nodes emit heartbeats (`POST /api/v1/heartbeat`) every 5 seconds.
- The Control Plane tracks active node topology and notifies nodes of membership changes so actor proxy invocations route to the correct node hosting the actor turn.

### 2. Multi-Instance Cluster Discovery Without External Meshes
In bare-metal, VM, or cross-cloud environments lacking Kubernetes DNS or Consul:
- Instances register dynamically with the Control Plane.
- Centra's [`ClusterTopologyProviderHostedService`](../../src/Centra.Sync/Sync/ClusterTopologyProviderHostedService.cs) continuously polls cluster state and feeds client-side round-robin load balancing.

### 3. Centralized Component Governance & Secret Resolution
When managing dozens of microservices with shared infrastructure:
- Instead of duplicating connection strings and credentials across dozens of YAML/JSON configs, components are defined once in the Control Plane catalog (`/api/v1/components`).
- The Control Plane resolves secret references from vault backends before broadcasting to node instances.

### 4. Real-Time SSE Hot-Reloading
When infrastructure changes cannot tolerate pod restarts:
- **Broker Migration**: Migrating a pub/sub broker or state store URL pushes immediate updates to all connected instances via `GET /api/v1/sync/stream`.
- **Dynamic Resilience Governance**: Polly Core v8 resilience policies (rate limit permits, circuit breaker failure ratios, timeout thresholds) can be adjusted on the fly via `POST /api/v1/resilience` and apply instantly across all nodes.

### 5. Centralized Operations & Diagnostics
The Control Plane exposes runtime inspection endpoints:
- Query active virtual actor counts across the cluster (`GET /api/v1/actors/activations`).
- Trigger manual passivation of idle or hot actors (`POST /api/v1/actors/{type}/{id}/passivate`).
- Query durable workflow instances and inspect historical event streams (`GET /api/v1/workflows/instances/{id}/history`).

---

### Code Example: Running With Control Plane

In your Aspire AppHost ([`Centra.AppHost`](../../samples/Centra.AppHost/Program.cs)):

```csharp
using Centra.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// 1. Add Control Plane Resource
var controlPlane = builder.AddCentraControlPlane("control-plane");

// 2. Wire applications to the Control Plane
var cluster = builder.AddProject<Projects.Centra_Sample_MultiInstance>("multi-instance-service")
    .WithCentra(controlPlane)
    .WithReplicas(3);

builder.Build().Run();
```

In your application code:

```csharp
// Full-stack Centra registration automatically enables Control Plane sync
builder.Services.AddCentra(options =>
{
    options.AppId = "orders-service";
});
```

---

## 🧭 Architecture Decision Flowchart

Use this flowchart to determine whether your deployment requires the Control Plane:

```mermaid
flowchart TD
    Start([New Centra Deployment]) --> Q1{Using Distributed Virtual Actors across multiple replicas?}
    Q1 -- Yes --> UseCP[Deploy Centra Control Plane]
    Q1 -- No --> Q2{Need live SSE hot-reloading for components or resilience policies?}
    Q2 -- Yes --> UseCP
    Q2 -- No --> Q3{Running on Kubernetes, Aspire, or static infrastructure with native DNS/secrets?}
    Q3 -- Yes --> NoCP[Run Standalone<br/>Direct Drivers + Configuration Resolver]
    Q3 -- No --> Q4{Need central catalog & secret resolution across VMs/hosts?}
    Q4 -- Yes --> UseCP
    Q4 -- No --> NoCP
```

---

## 🔗 Related Documentation

- [Centra Control Plane Architecture & REST API](control-plane.md)
- [.NET Aspire Orchestration Guide](../getting-started/aspire.md)
- [RabbitMQ Simulation Sample (Standalone Architecture)](../../samples/RabbitSimulation/README.md)
- [Multi-Instance Cluster Simulation (Orchestrated Architecture)](../../samples/Centra.Sample.MultiInstance/README.md)
- [Configuration & Options Reference](configuration-reference.md)
