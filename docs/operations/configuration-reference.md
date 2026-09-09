# Configuration & Options Reference

> Complete reference of configuration paradigms (declarative attributes vs. programmatic service collection extensions), strongly typed options classes, configuration properties, environment variable bindings, and JSON schema examples across Centra.

---

## 🧭 Configuration Paradigms: Attributes vs. Service Collection Extensions

Centra provides two distinct, complementary configuration paradigms:

1. **Declarative Configuration via Attributes**: Metadata is co-located directly on domain classes, interfaces, and methods using C# attributes. The assembly scanner automatically registers and configures components when calling `builder.Services.AddCentra()`.
2. **Programmatic Configuration via Service Collection Extensions**: Components, options, and infrastructure are explicitly wired using fluent `IServiceCollection` extension methods in startup code (`Program.cs`).

Both paradigms share the same underlying runtime primitives and driver contracts. You can use either paradigm exclusively or combine them freely across your services.

### Feature & Paradigm Comparison

| Dimension | Declarative Attributes | Programmatic `IServiceCollection` Extensions |
| :--- | :--- | :--- |
| **Primary Mechanism** | `[Topic]`, `[ServiceClient]`, `[Actor]`, `[Workflow]`, `[WorkflowActivity]`, `[CronBinding]`, `[Binding]` | `AddCentraEventHandler<T, E>()`, `AddCentraServiceClient<T>()`, `AddCentraActor<T, I>()`, `AddCentraWorkflow<T>()`, `AddCentraCronJob<T>()`, `AddCentraInputBindingHandler<T>()` |
| **Code Location** | Co-located with domain classes, interfaces, and methods | Centralized in `Program.cs` or modular DI extensions |
| **Discovery Mechanism** | Automatic reflection scan via `builder.Services.AddCentra()` | Explicit registration call per component |
| **Runtime / Dynamic Values** | Compile-time constants only (e.g., hardcoded string literals) | Dynamic (resolves values from `IConfiguration`, environment variables, databases) |
| **Modularity & ISP** | Full assembly scan across all concerns | Granular: register only needed concerns (`AddCentraPubSub()`, `AddCentraState()`, etc.) |
| **Precedence & Overrides** | Acts as baseline/default configuration | **Highest precedence**: explicit registrations override scanned attributes |
| **Testability & Mocking** | Fixed to class definition | Easy to substitute mock topics, staging stores, or fake clients in integration test fixtures |
| **Actor Interface Disambiguation** | Requires class to implement exactly one domain `IActor` interface | Can bind any arbitrary domain interface via `AddCentraActor<TActor, TActorInterface>()` |

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

  // In Program.cs - dynamically configured from appsettings:
  builder.Services.AddCentraPubSub();
  builder.Services.AddCentraEventHandler<OrderCreatedHandler, OrderCreatedEvent>(
      pubSubName: builder.Configuration["Messaging:BrokerName"],
      topic: builder.Configuration["Messaging:OrdersTopic"],
      deadLetterTopic: "custom.dlq",
      consumerMode: ConsumerMode.CompetingConsumer);
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

Binds to the `"Centra"` section in `appsettings.json` or `Centra__*` environment variables:

```json
{
  "Centra": {
    "AppId": "orders-service",
    "DefaultStateStore": "statestore",
    "DefaultPubSub": "pubsub",
    "DefaultLockStore": "lockstore",
    "ControlPlaneEndpoint": "http://control-plane:5000",
    "ControlPlane": {
      "Endpoint": "http://control-plane:5000",
      "InstanceId": "node-1",
      "HeartbeatIntervalSeconds": 5,
      "HeartbeatTimeoutSeconds": 15
    }
  }
}
```

| Property | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `AppId` | `string` | Entry assembly name | Logical application identifier for discovery and invocation |
| `DefaultStateStore` | `string` | `"statestore"` | Default state store component name |
| `DefaultPubSub` | `string` | `"pubsub"` | Default pub/sub component name |
| `DefaultLockStore` | `string` | `"lockstore"` | Default distributed lock store component name |
| `ControlPlaneEndpoint` | `string?` | `null` | URL of the Centra Control Plane service |
| `ControlPlane.InstanceId` | `string?` | Machine name / Guid | Unique replica identifier within the cluster |
| `ControlPlane.HeartbeatIntervalSeconds` | `int` | `5` | Heartbeat emission interval in seconds |

---

## 🎭 Virtual Actor Options (`ActorOptions`)

Configure via `builder.Services.AddCentraActors(options => ...)`:

```csharp
builder.Services.AddCentraActors(options =>
{
    options.ActorIdleTimeout = TimeSpan.FromMinutes(15);
    options.ActorScanInterval = TimeSpan.FromMinutes(1);
    options.DefaultStateStore = "statestore";
    options.ReminderLockExpiry = TimeSpan.FromSeconds(30);
});
```

| Property | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `ActorIdleTimeout` | `TimeSpan` | `15 minutes` | Inactivity duration before an actor is passivated |
| `ActorScanInterval` | `TimeSpan` | `1 minute` | Interval between passivation sweeps |
| `DefaultStateStore` | `string` | `"statestore"` | State store used to persist actor state and durable reminders |
| `ReminderLockExpiry` | `TimeSpan` | `30 seconds` | Distributed lock lease duration for reminder executions |

---

## 🔄 Workflow Engine Options (`WorkflowOptions`)

Configure via `builder.Services.AddCentraWorkflows(options => ...)`:

```csharp
builder.Services.AddCentraWorkflows(options =>
{
    options.DefaultStateStore = "statestore";
    options.ActivityDefaultTimeout = TimeSpan.FromSeconds(60);
});
```

| Property | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `DefaultStateStore` | `string` | `"statestore"` | State store used for workflow state records and history stream |
| `ActivityDefaultTimeout`| `TimeSpan` | `60 seconds` | Default timeout for activity execution steps |

---

## 🛡️ Resilience Options (`CentraResilienceOptions`)

Configure via `builder.Services.AddCentraResilience(options => ...)`:

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
