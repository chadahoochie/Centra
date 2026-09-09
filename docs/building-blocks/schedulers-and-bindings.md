# Distributed Schedulers & Bindings Engine

> Zero-allocation bitmask distributed cron scheduling, inbound webhook triggers, and resilient output bindings.

---

## 🎯 Key Capabilities

- **High-Precision Zero-Allocation Cron Parser**: Uses 64-bit integer masks (`ulong`) to compute next schedule occurrences with zero string allocations.
- **Cluster-Aware Single-Execution Cron Jobs**: Coordinates via distributed locks (`cron:{jobName}:{timestamp}`) so only one node in a cluster executes each tick.
- **Inbound Trigger Handlers**: Exposes `/centra/bindings/{bindingName}` to receive webhooks with automatic trace context extraction.
- **Resilient Output Bindings**: Outbound HTTP or message adapters wrapped in Polly v8 resilience pipelines and OpenTelemetry activities.

---

## ⏰ 1. Distributed Cron Scheduling

### Implementing a Job Handler
Implement `IJobHandler` and decorate `ExecuteAsync` with `[CronBinding]`:

```csharp
using Centra.Bindings;

public sealed class InventorySnapshotJob : IJobHandler
{
    private readonly ILogger<InventorySnapshotJob> _logger;

    public InventorySnapshotJob(ILogger<InventorySnapshotJob> logger)
    {
        _logger = logger;
    }

    // Runs every minute on the 0th second
    [CronBinding("0 * * * *")]
    public async ValueTask ExecuteAsync(ScheduledJobContext context)
    {
        _logger.LogInformation("Running distributed inventory snapshot for tick {TickTimeUtc}...",
            context.ScheduledTimeUtc);

        await DoInventorySnapshotWorkAsync(context.CancellationToken);
    }
}
```

### Supported Cron Syntax
- Standard 5-part: `* * * * *` (minute, hour, day-of-month, month, day-of-week)
- 6-part with seconds precision: `*/10 * * * * *` (every 10 seconds)
- Macros: `@every 30s`, `@hourly`, `@daily`, `@weekly`, `@monthly`, `@yearly`

### Registering Cron Jobs in `Program.cs`

Centra supports two ways to register cron jobs:

#### Option A: Declarative via `[CronBinding]` Attribute (Automatic Discovery)
When using `builder.Services.AddCentra()`, Centra automatically scans your assemblies for `IJobHandler` implementations whose `ExecuteAsync` method is decorated with `[CronBinding]`:

```csharp
// Auto-discovers and registers InventorySnapshotJob using its [CronBinding] expression
builder.Services.AddCentra();
```

#### Option B: Programmatic via `AddCentraCronJob` Extension (Explicit & Dynamic Overrides)
When using modular registrations without the umbrella metapackage, or when resolving cron expressions dynamically from configuration at runtime, register explicitly:

```csharp
builder.Services.AddCentraBindings();

// Explicit registration with dynamic cron expression from configuration
builder.Services.AddCentraCronJob<InventorySnapshotJob>(
    jobName: "inventory-sync",
    cronExpression: builder.Configuration["Schedules:InventorySyncCron"] ?? "0 * * * *",
    options: new CronScheduleOptions(MissedRunBehavior.Skip));
```

> [!TIP]
> Explicit parameters passed to `AddCentraCronJob` override the `[CronBinding]` attribute. Omitted parameters (`null`) fall back to the attribute.

---

## 📥 2. Inbound Binding Triggers

To expose an endpoint that receives external webhooks or events:

```csharp
using Centra.Bindings;

[Binding("github-webhooks", BindingDirection.Input)]
public sealed class GitHubWebhookTriggerHandler : IBindingTriggerHandler
{
    private readonly ILogger<GitHubWebhookTriggerHandler> _logger;

    public GitHubWebhookTriggerHandler(ILogger<GitHubWebhookTriggerHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<BindingResponse> HandleTriggerAsync(
        BindingData data, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received webhook payload: {Length} bytes, ContentType: {ContentType}",
            data.Data.Length, data.ContentType);

        // Process webhook...
        return new BindingResponse(ReadOnlyMemory<byte>.Empty);
    }
}
```

### Registering Inbound Triggers

- **Option A: Declarative via `[Binding]` Attribute**: Calling `builder.Services.AddCentra()` automatically discovers and registers handlers decorated with `[Binding(..., BindingDirection.Input)]`.
- **Option B: Programmatic via Extension**: When wiring modularly or overriding the binding name:
  ```csharp
  builder.Services.AddCentraBindings();
  builder.Services.AddCentraInputBindingHandler<GitHubWebhookTriggerHandler>("github-webhooks");
  ```

In `Program.cs`:
```csharp
var app = builder.Build();
app.MapCentraEndpoints(); // Exposes POST /centra/bindings/github-webhooks
```

---

## 📤 3. Resilient Output Bindings

To send data to an external service or webhook:

```csharp
builder.Services.AddCentraHttpWebhookBinding("slack-alerts");
```

In your application service:
```csharp
public sealed class AlertNotificationService
{
    private readonly IOutputBinding _outputBinding;

    public AlertNotificationService(IOutputBinding outputBinding)
    {
        _outputBinding = outputBinding;
    }

    public async Task SendAlertAsync(string alertMessage)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { text = alertMessage });
        var request = new BindingRequest("slack-alerts", payload, operation: "post");

        var response = await _outputBinding.InvokeAsync(request);
    }
}
```
All output binding calls automatically resolve the Polly v8 resilience pipeline for `binding:slack-alerts` and record OpenTelemetry duration metrics.
