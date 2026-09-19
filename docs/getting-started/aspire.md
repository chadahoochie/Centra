# .NET Aspire Cloud-Native Orchestration

> Learn how to configure and orchestrate Centra microservices and multi-replica clusters using .NET Aspire.

---

## 🌟 Overview

[.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) provides a developer dashboard, service discovery, container orchestration, and OpenTelemetry visualization for cloud-native .NET applications.

Centra provides first-class Aspire integration via the **`Centra.Aspire.Hosting`** package:
- Registers the **Centra Control Plane** as a launchable Aspire **container resource** that pulls the published control-plane image.
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

// 1. Add the Centra Control Plane container, pulled from the published image
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

## 📦 Two Ways to Run the Control Plane

`AddCentraControlPlane` produces a real `ContainerResource`, so Aspire starts it the same way it
starts Redis or RabbitMQ. Pick the route that matches how your solution consumes Centra.

### Route 1 — Pull the published image (default)

Use this when Centra is a dependency, not part of your solution — you reference the
`Centra.Aspire.Hosting` NuGet package and nothing else from Centra.

```csharp
var controlPlane = builder.AddCentraControlPlane("control-plane");
```

The defaults come from [`CentraControlPlaneImage`](../../src/Centra.Aspire.Hosting/CentraControlPlaneImage.cs):

| Setting | Default |
| --- | --- |
| Registry | `ghcr.io` |
| Image | `chadahoochie/centra-controlplane` |
| Tag | `1.0.0` — the Centra framework version (`VersionPrefix`, see `RELEASE_NOTES.md`) |
| Container port | `8080` |

The tag is immutable and moves only when the framework version does, so the default needs no
pull-policy tuning. Override any of the coordinates with Aspire's own container extensions — no
Centra-specific API required:

```csharp
var controlPlane = builder.AddCentraControlPlane("control-plane")
    .WithImageRegistry("myorg.azurecr.io")   // private mirror
    .WithImageTag("1.0.1");                  // a different published build
```

The image is published by the
[`Publish Control Plane Image`](../../.github/workflows/publish-controlplane-image.yml) workflow,
which builds [`src/Centra.ControlPlane/Dockerfile`](../../src/Centra.ControlPlane/Dockerfile) and
refuses any version that does not match `VersionPrefix`.

The published image is **`linux/amd64` only** — the publish workflow runs on an `ubuntu-latest`
runner and passes no `platforms:` list, so no arm64 manifest is pushed. On an arm64 host (Apple
Silicon, arm64 Linux, an arm64 cloud runner) Docker either refuses the manifest with

```text
no matching manifest for linux/arm64/v8 in the manifest list entries
```

or, where emulation is enabled, pulls the amd64 image and runs it under QEMU — functional but
noticeably slower to start. arm64 developers should prefer route 2, or build the image locally
from [`src/Centra.ControlPlane/Dockerfile`](../../src/Centra.ControlPlane/Dockerfile) and point at
it with `WithImage`/`WithImageRegistry`.

#### When the pull fails

Two distinct failures look similar in the Aspire dashboard but have different resolutions.

**1. The tag does not exist yet.** The image is published by a manual `workflow_dispatch` run, so
a given tag is pullable only after that workflow has been run for it. Docker reports:

```text
manifest unknown: manifest unknown
```

Resolution: run the `Publish Control Plane Image` workflow for the version in
`VersionPrefix`, or use route 2 below.

**2. The package exists but rejects an unauthenticated pull.** A GHCR package created by a first
push is **private by default**, and the AppHost's container pull carries no registry credentials.
Docker reports:

```text
denied: denied
unauthorized: unauthenticated: User cannot be authenticated with the token provided.
```

Resolution depends on an ownership decision that is **still pending with the repository owner**:

- **Public-package route (preferred, pending):** the GHCR package's visibility is set to public,
  after which unauthenticated pulls succeed and no consumer configuration is needed.
- **Documented-credential route:** the package stays private and consumers authenticate their
  local Docker daemon before running the AppHost —
  `echo $GHCR_PAT | docker login ghcr.io -u <username> --password-stdin`, using a PAT with
  `read:packages` and access to this repository's packages. Centra's AppHost path deliberately
  carries no registry credentials of its own, so this is a machine-level `docker login` rather
  than anything added to `AddCentraControlPlane`.

Until that decision lands, route 2 is the reliable path for consumers without access to this
repository's packages.

### Route 2 — Orchestrate the packaged project

Use this when Centra's source is already in your solution — you want to debug, patch, or extend
the control plane rather than consume a fixed build. This is what Centra's own sample AppHost
does ([`samples/Centra.AppHost/Program.cs`](../../samples/Centra.AppHost/Program.cs)):

```csharp
var controlPlane = builder.AddProject<Projects.Centra_ControlPlane>("control-plane")
    .WithHttpEndpoint(port: 8080, name: "http");

var orders = builder.AddProject<Projects.Centra_Sample_OrdersService>("orders-service")
    .WithCentra(controlPlane);   // binds via the IResourceWithEndpoints overload
```

`WithCentra` has an overload taking `IResourceBuilder<IResourceWithEndpoints>`, so a
`ProjectResource` works without any extra wiring.

---

## 🔍 How `.WithCentra(...)` Works

The extension method `WithCentra` (defined in `Centra.Aspire.Hosting.CentraAspireExtensions`) automatically sets:
1. `Centra__ControlPlaneEndpoint`: The dynamically assigned HTTP URL of the Control Plane container (route 1) or project (route 2).
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
