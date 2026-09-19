# RabbitMQ Provider

> Production AMQP pub/sub provider with CNCF CloudEvents headers mapping, durable exchanges, competing consumer groups, and dead-letter exchanges (DLX) via `RabbitMQ.Client`.

---

## 📦 Package

```xml
<PackageReference Include="Centra.Providers.RabbitMQ" />
```

---

## 🛠️ Registration

In `Program.cs`:

```csharp
using Centra.Providers.RabbitMQ.Extensions;

builder.Services.AddCentraRabbitMQ(options =>
{
    options.HostName = "localhost";
    options.Port = 5672;
    options.UserName = "guest";
    options.Password = "guest";
    options.VirtualHost = "/";
    options.ExchangeName = "centra.events";
    options.ExchangeType = "topic";
    options.Durable = true;
});
```

---

## 🔍 Implementation Highlights

### 1. AMQP Topic Exchange Mapping
- Centra publishes messages to a durable AMQP topic exchange (`centra.events`).
- The Centra topic string (e.g. `orders.created` or `customers.emea.updated`) directly maps to the AMQP routing key.

### 2. CNCF CloudEvents v1.0 Header Mapping
- CloudEvents metadata is mapped directly to AMQP `BasicProperties.Headers`:
  - `ce-id`
  - `ce-source`
  - `ce-type`
  - `ce-specversion`
  - `traceparent` (W3C TraceContext)
  - `tracestate`
- The message body contains the raw bytes of the domain payload, avoiding double-serialization.

### 3. Consumer Groups & Dead-Letter Exchanges (DLX)
- Handlers declare competing consumer queues bound to the topic routing key.
- Unhandled failures or `EventHandlingResult.DeadLetter` move messages to a configured Dead Letter Exchange (`centra.events.dlx`) for audit and manual replay.

### 4. Bounded Redelivery Budget
- Opt-in: `DefaultMaxRetryAttempts` is 0, so an unconfigured subscription keeps plain unbounded nack-requeue and declares its queue with no dead-letter arguments. Set `PubSubSubscribeOptions.MaxRetryAttempts` (or raise the provider default) and `EventHandlingResult.Retry` - plus any exception escaping the handler - is charged against a consumer-side budget with exponentially growing backoff from `DefaultRetryInitialBackoff` (1s) up to `DefaultRetryMaxBackoff` (30s), then the message is dead-lettered.
- The budget is enforced by the driver rather than by `x-delivery-limit`, because RabbitMQ advances `x-delivery-count` only when a delivery is returned by consumer or channel failure - an application `basic.nack(requeue=true)` never touches it, so a broker delivery limit cannot bound a retry loop. Keep `x-delivery-limit` on the queue only as a backstop against channel-failure loops.
- A dead-letter route is mandatory while the budget is in effect: `SubscribeAsync` throws when no `deadLetterTopic` is supplied, because a budget-exhausted message would otherwise be discarded and queue arguments cannot be changed after declare. Adding a budget to a subscription whose durable queue already exists therefore fails the redeclare with `406 PRECONDITION_FAILED`; recreate that queue.
- `[Topic]` and `AddCentraEventHandler` expose no retry-budget setting, so handlers registered that way are always unbudgeted; opt in through a direct `SubscribeAsync` call.
- `EventHandlingResult.Drop` acknowledges the delivery, so a dropped message is discarded rather than routed to the dead-letter exchange; only `DeadLetter` and budget exhaustion dead-letter.
- The backoff is awaited in the consumer callback, so with the default `MaxConcurrentCalls = 1` it also delays the other deliveries on that channel; `RetryMaxBackoff` bounds that head-of-line delay.
- Per-subscription overrides: `PubSubSubscribeOptions.MaxRetryAttempts`, `RetryInitialBackoff`, `RetryMaxBackoff`. See [Bounded Redelivery Budget](../building-blocks/pubsub.md#-bounded-redelivery-budget).
