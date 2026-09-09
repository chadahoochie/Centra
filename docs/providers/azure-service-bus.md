# Azure Service Bus Provider

> Cloud-native Azure Service Bus Pub/Sub provider with topics, subscriptions, CloudEvents application properties, and W3C tracecontext propagation via `Azure.Messaging.ServiceBus`.

---

## 📦 Package

```xml
<PackageReference Include="Centra.Providers.AzureServiceBus" />
```

---

## 🛠️ Registration

In `Program.cs`:

```csharp
using Centra.Providers.AzureServiceBus.Extensions;

builder.Services.AddCentraAzureServiceBus(options =>
{
    options.ConnectionString = "Endpoint=sb://your-namespace.servicebus.windows.net/;SharedAccessKeyName=...;SharedAccessKey=...";
    options.SubscriptionName = "orders-worker";
});
```

---

## 🔍 Implementation Highlights

### 1. Cloud-Native Topics & Subscriptions
- Each Centra pub/sub topic maps directly to an Azure Service Bus topic.
- Competing consumers share a named Service Bus subscription.
- Fan-out patterns create independent subscriptions per consuming service.

### 2. CloudEvents Metadata in Application Properties
- Message metadata is stored in `ServiceBusMessage.ApplicationProperties`:
  - `ce-id`
  - `ce-source`
  - `ce-type`
  - `ce-specversion`
  - Custom enterprise extensions (`ce-tenantid`, `ce-correlationid`)
- `traceparent` and `tracestate` are mapped into Service Bus properties, enabling seamless Azure Monitor and Application Insights end-to-end distributed tracing.

### 3. Message Settlement
- `EventHandlingResult.Success` -> calls `CompleteMessageAsync()`
- `EventHandlingResult.Retry` -> calls `AbandonMessageAsync()`
- `EventHandlingResult.DeadLetter` -> calls `DeadLetterMessageAsync()`
