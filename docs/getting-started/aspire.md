# .NET Aspire Cloud-Native Orchestration

> Learn how to configure and orchestrate Centra microservices and multi-replica clusters using .NET Aspire.

---

## 🌟 Overview

[.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) provides a developer dashboard, service discovery, container orchestration, and OpenTelemetry visualization for cloud-native .NET applications.

Centra provides first-class Aspire integration via the **`Centra.Aspire.Hosting`** package:
- Registers the **Centra Control Plane** as an Aspire resource.
- Injects environment variables (`Centra__ControlPlaneEndpoint`, `Centra__AppId`) automatically into application nodes via `.WithCentra(controlPlane)`.
- Seamlessly scales services to multiple replicas (`.WithReplicas(3)`).
- Captures OpenTelemetry traces and metrics in the Aspire Dashboard with zero manual telemetry code.

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    .NET Aspire AppHost                      │
│                                                             │
│   ┌───────────────────────────┐                             │
│   │    Centra Control Plane   │                             │
│   │ (Catalog, Topology, Sync) │                             │
│   └─────────────┬─────────────┘                             │
│                 │                                           │
│         .WithCentra(controlPlane)                           │
│                 │                                           │
│      ┌──────────┴──────────┐                                │
│      ▼                     ▼                                │
│ ┌───────────────┐   ┌───────────────────────────────┐       │
│ │ OrdersService │   │ MultiInstanceCluster (Replica)│       │
│ │   (1 Node)    │   │  - Node 1                     │       │
│ └───────────────┘   │  - Node 2                     │       │
│                     │  - Node 3                     │       │
│                     └───────────────────────────────┘       │
└─────────────────────────────────────────────────────────────┘
```

---

## 🛠️ Configuring the AppHost (`Program.cs`)

In your Aspire AppHost project (`Centra.AppHost`):

```csharp
using Centra.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// 1. Add the Centra Control Plane resource
var controlPlane = builder.AddCentraControlPlane("control-plane");

// 2. Add single-instance service wired to Control Plane
var orders = builder.AddProject<Projects.Centra_Sample_OrdersService>("orders-service")
    .WithCentra(controlPlane);

// 3. Add multi-instance cluster with 3 replicas
var cluster = builder.AddProject<Projects.Centra_Sample_MultiInstance>("multi-instance-service")
    .WithCentra(controlPlane)
    .WithReplicas(3);

builder.Build().Run();
```

---

## 🔍 How `.WithCentra(...)` Works

The extension method `WithCentra` (defined in `Centra.Aspire.Hosting.CentraAspireExtensions`) automatically sets:
1. `Centra__ControlPlaneEndpoint`: The dynamically assigned HTTP URL of the Aspire Control Plane container or process.
2. `Centra__AppId`: The logical application identifier matching the resource name in Aspire.

When the service boots, Centra's runtime automatically:
1. Connects to `IControlPlaneClient` at the provided endpoint.
2. Starts periodic heartbeat emissions (`/api/v1/heartbeat`) through `ClusterTopologyProviderHostedService`.
3. Opens a real-time Server-Sent Events (SSE) streaming sync subscription (`/api/v1/sync/stream`).
4. Updates local service discovery tables so peer instances immediately resolve each other.

---

## 🏃 Running with Aspire

Launch the AppHost project:

```bash
dotnet run --project samples/Centra.AppHost
```

Aspire prints the dashboard URL (typically `http://localhost:15000` or `http://localhost:17000` depending on dev certificate configuration).

### Features in the Aspire Dashboard:
- **Resources Tab**: View live statuses of `control-plane`, `orders-service`, and `multi-instance-service` (1, 2, 3).
- **Traces Tab**: Centra spans (`ActivitySource("Centra")`) with semantic tags (`centra.component = pubsub | state | lock | invoke | actors | workflows`) are displayed end-to-end.
- **Metrics Tab**: Real-time meters (`Meter("Centra")`) reporting counters, latency histograms, and lock holding durations.
- **Logs Tab**: Structured JSON logs generated via zero-allocation `[LoggerMessage]` source generators.

---

## 🚀 Next Steps

- Check out the [Docker Compose Cluster](docker-cluster.md) guide for running independent containers with real Redis, RabbitMQ, and Grafana.
- Review [Control Plane Architecture](../operations/control-plane.md).
