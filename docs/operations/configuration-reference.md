# Configuration & Options Reference

> Complete reference of configuration paradigms (declarative attributes vs. programmatic service collection extensions), strongly typed options classes, configuration properties, environment variable bindings, and JSON schema examples across Centra.

---

## 🧭 Configuration Paradigms: Attributes, Extensions & Builder Pattern

Centra provides three distinct, complementary configuration paradigms:

1. **Declarative Configuration via Attributes**: Metadata is co-located directly on domain classes, interfaces, and methods using C# attributes (`[Topic]`, `[ServiceClient]`, `[Actor]`, `[Workflow]`). The assembly scanner automatically registers and configures components when calling `builder.Services.AddCentra()`.
2. **Programmatic Configuration via Service Collection Extensions**: Components, options, and infrastructure are explicitly wired using modular `IServiceCollection` extension methods (`AddCentraPubSub()`, `AddCentraState()`, etc.) in startup code (`Program.cs`), adhering strictly to the Interface Segregation Principle (ISP).
3. **Fluent Config Builder via `ICentraBuilder`**: A unified, fluent configuration builder pattern returned by `builder.Services.AddCentra()` or configured via `builder.Services.AddCentraBuilder(...)`, allowing expressive method chaining for options and component registrations.

All three paradigms share the same underlying runtime primitives, driver contracts, and hierarchical options binding. You can use any paradigm exclusively or combine them freely across your services.

### Feature & Paradigm Comparison

| Dimension | Declarative Attributes | Modular `IServiceCollection` Extensions | Fluent `ICentraBuilder` Pattern |
| :--- | :--- | :--- | :--- |
| **Primary Mechanism** | `[Topic]`, `[ServiceClient]`, `[Actor]`, `[Workflow]`, `[WorkflowActivity]`, `[CronBinding]`, `[Binding]` | `AddCentraEventHandler<T, E>()`, `AddCentraServiceClient<T>()`, `AddCentraActor<T, I>()`, `AddCentraWorkflow<T>()`, `AddCentraCronJob<T>()` | `services.AddCentra().WithAppId(...).ConfigureState(...).AddActor<T, I>().AddWorkflow<T>()` |
| **Code Location** | Co-located with domain classes, interfaces, and methods | Centralized in `Program.cs` or modular DI extensions | Fluent chain in `Program.cs` |
| **Discovery Mechanism** | Automatic reflection scan via `builder.Services.AddCentra()` | Explicit registration call per component | Combined: auto-scans assemblies while allowing fluent overrides |
| **Runtime / Dynamic Values** | Compile-time constants only (e.g., hardcoded string literals) | Dynamic (resolves values from `IConfiguration`, environment variables, databases) | Dynamic (fluent lambdas, strongly typed subsystem options, providers) |
| **Modularity & ISP** | Full assembly scan across all concerns | Granular: register only needed concerns (`AddCentraPubSub()`, `AddCentraState()`, etc.) | Full-stack or modular chaining with provider builder extensions |
| **Precedence & Overrides** | Acts as baseline/default configuration | **Highest precedence**: explicit registrations override scanned attributes | **Highest precedence**: explicit chained registrations override scanned attributes |
| **Testability & Mocking** | Fixed to class definition | Easy to substitute mock topics, staging stores, or fake clients in integration test fixtures | Expressive chaining for testing fixtures (`.AddInMemory()`, `.ConfigureState(...)`) |
| **Actor Interface Disambiguation** | Requires class to implement exactly one domain `IActor` interface | Can bind any arbitrary domain interface via `AddCentraActor<TActor, TActorInterface>()` | Fluent binding via `.AddActor<TActor, TActorInterface>()` or `.AddActor(type, iface)` |

---

### Precedence & Override Mechanics

Centra's DI registration system is strictly **idempotent** and adheres to the **explicit-wins** principle:

1. **Explicit Calls Win**: If an assembly scanner discovers a class decorated with an attribute (e.g., `[Topic("pubsub", "orders.created")]`), but `Program.cs` also contains an explicit call:
   ```csharp
   builder.Services.AddCentraEventHandler<PaymentNotificationHandler, OrderCreatedEvent>(
       pubSubName: builder.Configuration["PubSub:ComponentName"] ?? "orders-bus",
       topic: "tenants.eu.orders");
   ```
   The explicit call **overwrites** the attribute registration in DI. No duplicate consumers or subscriptions are created.
2. **Cascading Fallbacks**: In explicit extension methods, optional parameters default to `null`. When an argument is `null`, Centra inspects the attribute on the class. If no attribute is found, it falls back to framework defaults:
   - Topic name: Explicit parameter ➔ `[Topic]` attribute ➔ Event type name (`typeof(TEvent).Name`)
   - Pub/Sub component: Explicit parameter ➔ `[Topic]` attribute ➔ `options.DefaultPubSub` ("pubsub")
   - Dead-letter topic: Explicit parameter ➔ `[Topic]` attribute ➔ None (`null`)
   - Consumer mode: Explicit parameter ➔ `[Topic]` attribute ➔ `ConsumerMode.CompetingConsumer`
3. **Scan Execution Order**: Calling `builder.Services.AddCentra(...)` runs the scanner during that invocation. Any explicit calls placed after or before `AddCentra(...)` will safely override the attribute metadata because `ReplaceRegistrationFor<T>` replaces registrations by matching handler or actor type.

---

### Side-by-Side Reference by Concern

#### 1. Pub/Sub Event Handlers

- **Declarative via Attribute**:
  ```csharp
  [Topic("pubsub", "orders.created", deadLetterTopic: "orders.dlq")]
  public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
  {
      public Task<EventHandlingResult> HandleAsync(OrderCreatedEvent @event, EventContext context, CancellationToken ct)
          => Task.FromResult(EventHandlingResult.Success);
  }

  // In Program.cs - auto-discovered by AddCentra:
  builder.Services.AddCentra();
  ```

- **Programmatic via Extension**:
  ```csharp
  // Handler has no attributes:
  public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent> { /* ... */ }

  // In Program.cs - dynamically configured from appsettings with optional rule filtering:
  builder.Services.AddCentraPubSub();
  builder.Services.AddCentraEventHandler<OrderCreatedHandler, OrderCreatedEvent>(
      pubSubName: builder.Configuration["Messaging:BrokerName"],
      topic: builder.Configuration["Messaging:OrdersTopic"],
      deadLetterTopic: "custom.dlq",
      consumerMode: ConsumerMode.CompetingConsumer,
      ruleFilter: "data.totalAmount >= 500",
      priority: 10);
  ```

#### 2. Typed RPC Clients

- **Declarative via Attribute**:
  ```csharp
  [ServiceClient("inventory-service")]
  public interface IInventoryClient
  {
      [ServiceMethod("items/{id}/stock", "GET")]
      Task<StockResponse> GetStockAsync(string id);
  }

  // In Program.cs - auto-discovered by AddCentra:
  builder.Services.AddCentra();
  ```

- **Programmatic via Extension**:
  ```csharp
  // In Program.cs - explicit registration (e.g. inside a modular library or unit test):
  builder.Services.AddCentraInvocation();
  builder.Services.AddCentraServiceClient<IInventoryClient>();
  ```

#### 3. Distributed Virtual Actors

- **Declarative via Attribute**:
  ```csharp
  [Actor("AccountActor")]
  public sealed class AccountActor : Actor, IAccountActor
  {
      public ValueTask<decimal> GetBalanceAsync() => /* ... */;
  }

  // In Program.cs - auto-discovered by AddCentra:
  builder.Services.AddCentra();
  ```

- **Programmatic via Extension**:
  ```csharp
  // In Program.cs - explicitly pair actor implementation with domain interface:
  builder.Services.AddCentraActors(options => options.ActorIdleTimeout = TimeSpan.FromMinutes(30));
  builder.Services.AddCentraActor<AccountActor, IAccountActor>();
  ```

#### 4. Workflows & Activities

- **Declarative via Attribute**:
  ```csharp
  [Workflow("OrderProcessingWorkflow")]
  public sealed class OrderProcessingWorkflow : Workflow<OrderRequest, OrderResult> { /* ... */ }

  [WorkflowActivity("ProcessPayment")]
  public sealed class ProcessPaymentActivity : WorkflowActivity<PaymentRequest, PaymentResult> { /* ... */ }

  // In Program.cs - auto-discovered by AddCentra:
  builder.Services.AddCentra();
  ```

- **Programmatic via Extension**:
  ```csharp
  // In Program.cs - explicit registration:
  builder.Services.AddCentraWorkflows(options => options.DefaultStateStore = "statestore");
  builder.Services.AddCentraWorkflow<OrderProcessingWorkflow>();
  builder.Services.AddCentraWorkflowActivity<ProcessPaymentActivity>();
  ```

#### 5. Distributed Schedulers & Cron Jobs

- **Declarative via Attribute**:
  ```csharp
  public sealed class DailyReconciliationJob : IJobHandler
  {
      [CronBinding("0 0 * * *")]
      public ValueTask ExecuteAsync(ScheduledJobContext context) => ValueTask.CompletedTask;
  }

  // In Program.cs - auto-discovered by AddCentra:
  builder.Services.AddCentra();
  ```

- **Programmatic via Extension**:
  ```csharp
  // In Program.cs - dynamic cron expression from configuration or database:
  builder.Services.AddCentraBindings();
  builder.Services.AddCentraCronJob<DailyReconciliationJob>(
      jobName: "daily-recon",
      cronExpression: builder.Configuration["Schedules:ReconciliationCron"] ?? "0 0 * * *",
      options: new CronScheduleOptions(MissedRunBehavior.Skip));
  ```

#### 6. Inbound Webhook Triggers

- **Declarative via Attribute**:
  ```csharp
  public sealed class GitHubWebhookHandler : IBindingTriggerHandler
  {
      [Binding("github-webhook", BindingDirection.Input)]
      public ValueTask<BindingResponse> HandleTriggerAsync(BindingData data, CancellationToken ct) => /* ... */;
  }

  // In Program.cs - auto-discovered by AddCentra:
  builder.Services.AddCentra();
  ```

- **Programmatic via Extension**:
  ```csharp
  // In Program.cs - explicit binding registration:
  builder.Services.AddCentraBindings();
  builder.Services.AddCentraInputBindingHandler<GitHubWebhookHandler>("github-webhook");
  ```

---

### Decision Guide: Which Should You Use?

| Use Declarative Attributes When... | Use Service Collection Extensions When... |
| :--- | :--- |
| Configuration is static and identical across environments (e.g. standard topic names, workflow names). | Topic, broker, or schedule names differ by environment (e.g. development vs. staging vs. production) or come from `appsettings.json` / environment variables. |
| You want clean, self-documenting domain classes where consumers immediately see messaging routing and contracts. | You adhere strictly to the **Interface Segregation Principle (ISP)** and only want to register specific building blocks (e.g., `AddCentraPubSub()` only). |
| You prefer minimal boilerplate in `Program.cs` with a single `AddCentra()` call. | You are writing **integration tests** (`WebApplicationFactory`) and need to override topic names, mock external clients, or point to testcontainers. |
| An actor class implements exactly one domain interface derived from `IActor`. | An actor class implements multiple domain interfaces and you need to specify which one to expose via DI. |
| You want fast prototyping and domain-driven design ergonomics. | You are authoring reusable class libraries or NuGet packages that expose extensible registration methods for downstream consumers. |

---

## ⚙️ Core Configuration (`CentraOptions`)

`CentraOptions` serves as the root configuration model for the Centra Distributed Application Framework. It supports both top-level convenience properties (which seamlessly forward to their respective subsystem option objects for backwards compatibility) and strongly-typed hierarchical sections for all major concerns.

### Hierarchical `appsettings.json` Schema

Centra automatically binds the `"Centra"` section in `appsettings.json` (or double-underscore environment variables such as `Centra__State__DefaultStore`) to `CentraOptions` and its child options:

```json
{
  "Centra": {
    "AppId": "orders-service",
    "DefaultCloudEventMode": "Binary",
    "DefaultStateStore": "statestore",
    "DefaultPubSub": "pubsub",
    "DefaultLockStore": "lockstore",
    "ControlPlaneEndpoint": "http://control-plane:5000",
    "State": {
      "DefaultStore": "statestore"
    },
    "PubSub": {
      "DefaultStore": "pubsub"
    },
    "Locks": {
      "DefaultStore": "lockstore",
      "DefaultExpiry": "00:00:30",
      "DefaultTimeout": "00:00:05"
    },
    "ControlPlane": {
      "Endpoint": "http://control-plane:5000",
      "Endpoints": ["http://control-plane-1:5000", "http://control-plane-2:5000"],
      "ClusterId": "default",
      "InstanceId": "node-1",
      "HeartbeatInterval": "00:00:05",
      "EnableLiveSync": true,
      "ClusterToken": "secret-token",
      "UseHmacAuthentication": false
    },
    "Actors": {
      "ActorIdleTimeout": "00:15:00",
      "ActorScanInterval": "00:00:30",
      "DrainTimeout": "00:00:30",
      "ReminderInterval": "00:00:10",
      "DefaultStateStore": "statestore",
      "DefaultLockStore": "lockstore"
    },
    "Workflows": {
      "DefaultExecutionTimeout": "00:30:00",
      "PollingInterval": "00:00:01",
      "DefaultStateStore": "statestore",
      "DefaultPubSub": "pubsub",
      "DefaultLockStore": "lockstore"
    }
  }
}
```

### `CentraOptions` Root Properties

| Property | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `AppId` | `string` | `"centra-app"` | Logical application identifier for discovery and invocation |
| `DefaultCloudEventMode` | `CloudEventMode` | `Binary` | Default CloudEvents serialization mode (`Binary` or `Structured`) |
| `DefaultStateStore` | `string` | `"statestore"` | Forwards to `State.DefaultStore` |
| `DefaultPubSub` | `string` | `"pubsub"` | Forwards to `PubSub.DefaultStore` |
| `DefaultLockStore` | `string` | `"lockstore"` | Forwards to `Locks.DefaultStore` |
| `ControlPlaneEndpoint` | `string?` | `null` | Forwards to `ControlPlane.Endpoint` |
| `ClusterId` | `string` | `"default"` | Forwards to `ControlPlane.ClusterId` |
| `ControlPlane` | `CentraControlPlaneOptions` | `new()` | Control plane connection and cluster coordination options |
| `State` | `CentraStateOptions` | `new()` | Options for state persistence and optimistic concurrency |
| `PubSub` | `CentraPubSubOptions` | `new()` | Options for pub/sub messaging and CloudEvents routing |
| `Locks` | `CentraLocksOptions` | `new()` | Options for distributed locks and lease timeouts |
| `Actors` | `ActorOptions` | `new()` | Options for virtual actor turn mailbox and passivation |
| `Workflows` | `WorkflowOptions` | `new()` | Options for distributed workflows and saga coordination |
| `Resilience` | `CentraResilienceOptions` | `new()` | Options for Polly Core v8 composite resilience policies |

---

## 🎛️ Control Plane Options (`CentraControlPlaneOptions`)

Package: `Centra.Runtime` | Namespace: `Centra`

| Property | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Endpoint` | `string?` | `null` | URL of the primary Centra Control Plane service |
| `Endpoints` | `IReadOnlyList<string>` | `[]` | Multi-endpoint list of Control Plane replicas for client-side HA failover |
| `HeartbeatInterval` | `TimeSpan` | `5 seconds` | Heartbeat emission interval |
| `EnableLiveSync` | `bool` | `true` | Enables real-time SSE hot-reload event consumption |
| `ClusterId` | `string` | `"default"` | Target cluster partition identifier for topology and actor hash ring isolation |
| `InstanceId` | `string` | `Guid.NewGuid().ToString("N")` | Unique replica identifier within the cluster |
| `ClusterToken` | `string?` | `null` | Pre-shared token or HMAC secret for rogue node admission defense |
| `UseHmacAuthentication` | `bool` | `false` | Enables cryptographic timestamp + nonce + HMAC-SHA256 signature verification |
| `Metadata` | `Dictionary<string, string>` | `new()` | Arbitrary replica metadata sent during heartbeats |

---

## 💾 State Store Options (`CentraStateOptions`)

Package: `Centra.State.Abstractions` | Namespace: `Centra.State`

| Property | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `DefaultStore` | `string` | `"statestore"` | Default state store component name for state operations |

---

## 📨 Pub/Sub Messaging Options (`CentraPubSubOptions`)

Package: `Centra.PubSub.Abstractions` | Namespace: `Centra.PubSub`

| Property | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `DefaultStore` | `string` | `"pubsub"` | Default pub/sub broker component name for event publishing and subscription |

---

## 🔒 Distributed Locks Options (`CentraLocksOptions`)

Package: `Centra.Locks.Abstractions` | Namespace: `Centra.Locks`

| Property | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `DefaultStore` | `string` | `"lockstore"` | Default distributed lock store component name |
| `DefaultExpiry` | `TimeSpan` | `30 seconds` | Default lease expiry duration when acquiring locks |
| `DefaultTimeout` | `TimeSpan` | `5 seconds` | Default wait timeout when attempting to acquire a lock |

---

## 🎭 Virtual Actor Options (`ActorOptions`)

Package: `Centra.Actors.Abstractions` | Namespace: `Centra.Actors`

Configured via `services.AddCentraActors(options => ...)` or builder `.ConfigureActors(options => ...)`:

```csharp
builder.Services.AddCentraActors(options =>
{
    options.ActorIdleTimeout = TimeSpan.FromMinutes(20);
    options.ActorScanInterval = TimeSpan.FromSeconds(30);
    options.DrainTimeout = TimeSpan.FromSeconds(30);
    options.ReminderInterval = TimeSpan.FromSeconds(10);
    options.DefaultStateStore = "custom-statestore";
    options.DefaultLockStore = "custom-lockstore";
});
```

| Property | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `ActorIdleTimeout` | `TimeSpan` | `15 minutes` | Inactivity duration before an idle actor is passivated |
| `ActorScanInterval` | `TimeSpan` | `30 seconds` | Periodic interval between idle actor passivation sweeps |
| `DrainTimeout` | `TimeSpan` | `30 seconds` | Maximum duration allowed to drain active mailbox turns during passivation or shutdown |
| `ReminderInterval` | `TimeSpan` | `10 seconds` | Polling tick interval for evaluating and dispatching durable reminders |
| `DefaultStateStore` | `string` | `"statestore"` | State store used to persist actor state and durable reminders (cascades from `CentraOptions.DefaultStateStore`) |
| `DefaultLockStore` | `string` | `"lockstore"` | Distributed lock store used for reminder leader election (cascades from `CentraOptions.DefaultLockStore`) |
| `HasExplicitStateStore` | `bool` | `false` | Indicates whether `DefaultStateStore` was explicitly overridden away from `"statestore"` |
| `HasExplicitLockStore` | `bool` | `false` | Indicates whether `DefaultLockStore` was explicitly overridden away from `"lockstore"` |

---

## 🔄 Workflow Engine Options (`WorkflowOptions`)

Package: `Centra.Workflows.Abstractions` | Namespace: `Centra.Workflows`

Configured via `services.AddCentraWorkflows(options => ...)` or builder `.ConfigureWorkflows(options => ...)`:

```csharp
builder.Services.AddCentraWorkflows(options =>
{
    options.DefaultExecutionTimeout = TimeSpan.FromHours(1);
    options.PollingInterval = TimeSpan.FromSeconds(2);
    options.DefaultStateStore = "custom-statestore";
    options.DefaultPubSub = "custom-pubsub";
    options.DefaultLockStore = "custom-lockstore";
});
```

| Property | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `DefaultExecutionTimeout` | `TimeSpan` | `30 minutes` | Default timeout ceiling for an entire workflow orchestration run |
| `PollingInterval` | `TimeSpan` | `1 second` | Polling interval for evaluating durable timers and checking pending external event completions |
| `DefaultStateStore` | `string` | `"statestore"` | State store used for workflow execution history and timer storage (cascades from `CentraOptions.DefaultStateStore`) |
| `DefaultPubSub` | `string` | `"pubsub"` | Pub/Sub broker used for external workflow event subscriptions (cascades from `CentraOptions.DefaultPubSub`) |
| `DefaultLockStore` | `string` | `"lockstore"` | Distributed lock store used for durable timer coordinator locks (cascades from `CentraOptions.DefaultLockStore`) |
| `HasExplicitStateStore` | `bool` | `false` | Indicates whether `DefaultStateStore` was explicitly overridden away from `"statestore"` |
| `HasExplicitPubSub` | `bool` | `false` | Indicates whether `DefaultPubSub` was explicitly overridden away from `"pubsub"` |
| `HasExplicitLockStore` | `bool` | `false` | Indicates whether `DefaultLockStore` was explicitly overridden away from `"lockstore"` |

---

## 🌊 Automatic Store Cascading & Post-Configuration

In enterprise distributed applications, configuring the same state store, lock store, or pub/sub broker name repeatedly across multiple subsystems introduces friction and configuration drift.

Centra uses Microsoft.Extensions.Options post-configuration (`ActorOptionsPostSetup` and `WorkflowOptionsPostSetup`) to automatically **cascade root store defaults**:

1. **Automatic Fallback to Root Defaults**: If an application sets `DefaultStateStore = "production-pg"` on `CentraOptions`, both `ActorOptions.DefaultStateStore` and `WorkflowOptions.DefaultStateStore` automatically adopt `"production-pg"` unless specifically configured otherwise.
2. **Explicit Wins**: If configuration explicitly defines `Centra:Actors:DefaultStateStore = "actor-redis"`, or code executes `builder.ConfigureActors(a => a.DefaultStateStore = "actor-redis")`, the explicit setting wins and overrides the cascade.
3. **Precedence Hierarchy**:
   ```
   [Explicit Configuration Key] (e.g. Centra:Actors:DefaultStateStore)
            │ (if unset)
            ▼
   [Explicit Programmatic Call] (e.g. options.DefaultStateStore = "custom")
            │ (if default)
            ▼
   [Root CentraOptions Setting] (e.g. CentraOptions.DefaultStateStore)
            │ (if unset)
            ▼
   [Framework Global Default] ("statestore", "pubsub", "lockstore")
   ```

---

## 🏗️ Fluent Config Builder (`ICentraBuilder`)

Calling `builder.Services.AddCentra(...)` returns an `ICentraBuilder` instance, allowing clean, fluent chaining of all configuration options, components, and provider drivers:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCentra()
    .WithAppId("orders-service")
    .WithCloudEventMode(CloudEventMode.Binary)
    .ConfigureControlPlane(cp => cp.Endpoint = "http://control-plane:5000")
    .ConfigureState(s => s.DefaultStore = "cluster-statestore")
    .ConfigurePubSub(p => p.DefaultStore = "cluster-pubsub")
    .ConfigureLocks(l => l.DefaultStore = "cluster-lockstore")
    .ConfigureActors(a => a.ActorIdleTimeout = TimeSpan.FromMinutes(20))
    .AddActor<AccountActor, IAccountActor>()
    .ConfigureWorkflows(w => w.DefaultExecutionTimeout = TimeSpan.FromHours(1))
    .AddWorkflow<OrderWorkflow>()
    .AddWorkflowActivity<ReserveInventoryActivity>()
    .AddEventHandler<PaymentNotificationHandler, OrderCreatedEvent>()
    .AddServiceClient<IInventoryClient>()
    .ConfigureResilience(r => r.AddPolicy(new CentraResiliencePolicyDefinition("orders-retry", ...)))
    .AddRedis(redis => redis.Configuration = "redis:6379");
```

Alternatively, you can use the action-based syntax via `AddCentraBuilder`:

```csharp
builder.Services.AddCentraBuilder(centra =>
{
    centra.WithAppId("orders-service")
          .ConfigureActors(a => a.ActorIdleTimeout = TimeSpan.FromMinutes(20))
          .AddInMemory();
});
```

### `ICentraBuilder` Extension Methods Reference

| Extension Method | Assembly | Description |
| :--- | :--- | :--- |
| `.WithAppId(string)` | `Centra.Runtime` | Sets the application identifier in `CentraOptions.AppId` |
| `.WithCloudEventMode(CloudEventMode)` | `Centra.Runtime` | Sets default CloudEvents serialization mode (`Binary` or `Structured`) |
| `.Configure(Action<CentraOptions>)` | `Centra.Runtime` | Configures root `CentraOptions` |
| `.ConfigureControlPlane(Action<CentraControlPlaneOptions>)` | `Centra.Runtime` | Configures Control Plane coordination and sync options |
| `.ConfigureState(Action<CentraStateOptions>)` | `Centra.Runtime` | Configures State Store building block options |
| `.ConfigurePubSub(Action<CentraPubSubOptions>)` | `Centra.Runtime` | Configures Pub/Sub messaging building block options |
| `.ConfigureLocks(Action<CentraLocksOptions>)` | `Centra.Runtime` | Configures Distributed Locks building block options |
| `.ConfigureActors(Action<ActorOptions>)` | `Centra.Actors` | Configures Virtual Actors runtime options |
| `.AddActor<TActor, TActorInterface>()` | `Centra.Actors` | Registers a virtual actor implementation and its domain `IActor` interface |
| `.AddActor(Type, Type)` | `Centra.Actors` | Registers a virtual actor type and its domain interface dynamically |
| `.ConfigureWorkflows(Action<WorkflowOptions>)` | `Centra.Workflows` | Configures Distributed Workflows and Sagas runtime options |
| `.AddWorkflow<TWorkflow>()` | `Centra.Workflows` | Registers a distributed workflow orchestration in DI and the workflow registry |
| `.AddWorkflowActivity<TActivity>()` | `Centra.Workflows` | Registers a workflow activity step in DI and the activity registry |
| `.AddEventHandler<THandler, TEvent>(...)` | `Centra.PubSub` | Registers a pub/sub event handler with optional topic, filter, and priority |
| `.AddOutbox(Action<OutboxOptions>?)` | `Centra.PubSub` | Adds transactional outbox publisher background service and repository |
| `.AddInbox(Action<InboxOptions>?)` | `Centra.PubSub` | Adds idempotent inbox deduplication background cleanup and repository |
| `.AddTenantOffload(Action<TenantOffloadOptions>?)` | `Centra.PubSub` | Adds multi-tenant noisy neighbor offload coordinator and dynamic dispatcher |
| `.ConfigureResilience(Action<CentraResilienceOptions>)` | `Centra.Resilience` | Configures Resilience policies and default pipelines |
| `.AddResiliencePolicy(CentraResiliencePolicyDefinition)` | `Centra.Resilience` | Adds a resilience policy definition |
| `.AddServiceClient<TInterface>()` | `Centra.Invocation` | Registers a strongly typed RPC client proxy for a `[ServiceClient]` interface |
| `.AddInMemory(...)` | `Centra.Providers.InMemory` | Registers zero-dependency in-memory providers for testing |
| `.AddRedis(Action<RedisProviderOptions>?)` | `Centra.Providers.Redis` | Registers Redis for state store, pub/sub, and distributed locks |
| `.AddRedisStateStore(string, ...)` | `Centra.Providers.Redis` | Registers a named Redis State Store component |
| `.AddRedisPubSub(string, ...)` | `Centra.Providers.Redis` | Registers a named Redis Pub/Sub broker component |
| `.AddRedisLocks(string, ...)` | `Centra.Providers.Redis` | Registers a named Redis Distributed Lock store component |

---

## 🛡️ Resilience Options (`CentraResilienceOptions`)

Package: `Centra.Resilience.Abstractions` | Namespace: `Centra.Resilience`

Configure via `builder.Services.AddCentraResilience(options => ...)` or builder `.ConfigureResilience(options => ...)`:

```csharp
builder.Services.AddCentraResilience(options =>
{
    options.AddPolicy(new CentraResiliencePolicyDefinition(
        PolicyName: "my-policy",
        Retry: new RetryPolicyOptions(
            MaxRetries: 3,
            BackoffType: CentraBackoffType.Exponential,
            BaseDelay: TimeSpan.FromMilliseconds(50),
            MaxDelay: TimeSpan.FromSeconds(2),
            UseJitter: true),
        CircuitBreaker: new CircuitBreakerPolicyOptions(
            FailureRatio: 0.5,
            SamplingDuration: TimeSpan.FromSeconds(10),
            MinimumThroughput: 5,
            BreakDuration: TimeSpan.FromSeconds(5)),
        RateLimiter: new RateLimiterPolicyOptions(
            PermitLimit: 100,
            Window: TimeSpan.FromSeconds(1)),
        Timeout: new TimeoutPolicyOptions(TimeSpan.FromSeconds(5))));
});
```

| Property / Method | Type | Description |
| :--- | :--- | :--- |
| `Policies` | `IReadOnlyList<CentraResiliencePolicyDefinition>` | Read-only list of configured resilience policy definitions |
| `AddPolicy(definition)` | `CentraResilienceOptions` | Fluent method to add a policy definition to the options |

---

## 🏢 Multi-Tenant Pub/Sub Offload Options (`TenantOffloadOptions`)

Governs real-time noisy neighbor tenant detection, fair scheduling, and broker topic partitioning:

```csharp
builder.Services.AddCentraTenantOffload(options =>
{
    options.WindowDuration = TimeSpan.FromSeconds(60);
    options.MinSampleCount = 20;
    options.TrafficShareThreshold = 0.30;
    options.DurationMultiplierThreshold = 3.0;
    options.DurationAbsoluteThresholdMs = 2500;
    options.CooldownPeriod = TimeSpan.FromSeconds(30);
    options.OffloadStrategy = TenantOffloadStrategyType.InProcessFairScheduler;
    options.MaxConcurrencyPerTenant = 2;
    options.PerTenantQueueCapacity = 500;
    options.OffloadShardCount = 4;
    options.LaneIdleTimeout = TimeSpan.FromSeconds(60);
    options.OffloadTopicIdleTtl = TimeSpan.FromMinutes(5);
    options.EnablePublisherBypassing = true;
    options.OffloadTopicPattern = "{topic}.offload.{tenantId}";
});
```

| Property | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `WindowDuration` | `TimeSpan` | `60s` | Duration of the sliding evaluation window for rate and duration metrics. |
| `MinSampleCount` | `int` | `20` | Minimum event samples required in window before offload triggers can fire. |
| `TrafficShareThreshold` | `double` | `0.30` | Maximum fraction of topic traffic (0.0 to 1.0) allowed for one tenant before offload. |
| `DurationMultiplierThreshold` | `double` | `3.0` | Factor of average processing duration that marks a tenant's operations as disproportionately slow. |
| `DurationAbsoluteThresholdMs` | `double?` | `null` | Optional absolute processing duration cap in milliseconds. |
| `CooldownPeriod` | `TimeSpan` | `30s` | Minimum duration a tenant remains offloaded before evaluating recovery back to normal. |
| `OffloadStrategy` | `TenantOffloadStrategyType` | `InProcessFairScheduler` | Strategy: `InProcessFairScheduler`, `BoundedShardBrokerTopic`, or `EphemeralBrokerTopic`. |
| `MaxConcurrencyPerTenant` | `int` | `2` | Maximum concurrent invocations in a tenant's isolated worker lane. |
| `PerTenantQueueCapacity` | `int` | `500` | Maximum pending work items in a tenant worker lane before applying backpressure. |
| `OffloadShardCount` | `int` | `4` | Number of physical broker topic shards when using `BoundedShardBrokerTopic`. |
| `LaneIdleTimeout` | `TimeSpan` | `60s` | Inactivity threshold before the reaper drains and disposes an idle tenant worker lane. |
| `OffloadTopicIdleTtl` | `TimeSpan` | `5m` | Auto-delete TTL applied to ephemeral broker topics. |
| `EnablePublisherBypassing` | `bool` | `true` | Allows `CentraPubSubClient` to steer outgoing messages directly to offload topic partitions. |
| `OffloadTopicPattern` | `string` | `"{topic}.offload.{tenantId}"` | Formatting template for ephemeral or dedicated broker topics. |

---

## 🔌 Provider Options Reference

### Redis (`RedisProviderOptions`)
```csharp
builder.Services.AddCentraRedis(options =>
{
    options.Configuration = "localhost:6379";
    options.ConnectionString = "localhost:6379,password=secret";
    options.InstanceName = "app:";
    options.DefaultDatabase = 0;
});
```

### PostgreSQL (`PostgreSqlProviderOptions`)
```csharp
builder.Services.AddCentraPostgreSql(options =>
{
    options.ConnectionString = "Host=localhost;Database=centra;Username=postgres;Password=postgres";
    options.Schema = "centra";
    options.AutoCreateSchema = true;
});
```

### RabbitMQ (`RabbitMQProviderOptions`)
```csharp
builder.Services.AddCentraRabbitMQ(options =>
{
    options.HostName = "localhost";
    options.Port = 5672;
    options.UserName = "guest";
    options.Password = "guest";
    options.ExchangeName = "centra.events";
    options.ExchangeType = "topic";
    options.Durable = true;
});
```

### SQL Server (`SqlServerProviderOptions`)
```csharp
builder.Services.AddCentraSqlServer(options =>
{
    options.ConnectionString = "Server=localhost,1433;Database=centra;User Id=sa;Password=secret;TrustServerCertificate=True;";
    options.SchemaName = "dbo";
    options.AutoCreateTable = true;
});
```

### Azure Service Bus (`AzureServiceBusProviderOptions`)
```csharp
builder.Services.AddCentraAzureServiceBus(options =>
{
    options.ConnectionString = "Endpoint=sb://namespace.servicebus.windows.net/;...";
    options.SubscriptionName = "my-service-worker";
});
```

### Azure Cosmos DB (`CosmosDbProviderOptions`)
```csharp
builder.Services.AddCentraCosmosDb(options =>
{
    options.ConnectionString = "AccountEndpoint=https://account.documents.azure.com:443/;AccountKey=...;";
    options.DatabaseName = "centra";
    options.AutoCreateDatabaseAndContainers = true;
});
```
