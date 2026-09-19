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

### 4. Graceful Consumer Drain on Shutdown
- Unsubscribing or disposing does not simply close the channel. Each subscription is torn down in order: `basic.cancel` the consumer so the broker stops dispatching, wait for `cancel-ok` so the client finishes handing already-prefetched deliveries to handlers, await the in-flight handlers, and only then close the channel.
- `TotalShutdownDrainTimeout` (default `10s`) bounds the handler drain. It is a **total** allowance for the whole shutdown, not per subscription: [`CentraRuntimeHostedService`](../../src/Centra.Hosting/HostedServices/CentraRuntimeHostedService.cs) opens one shared drain window around its teardown loop (via `IPubSubShutdownDrain`), so the drain cost never scales with the number of subscribed topics. A standalone `UnsubscribeAsync` outside a shutdown gets the full allowance for that one subscription.
- The `basic.cancel` RPC and the channel close are control-plane steps outside that budget — they are bounded by the caller's cancellation token and the client's own continuation timeout — so a consumer is always cancelled even once the drain allowance is spent.
- Every configured value has a defined meaning, with no silent degradation and no shutdown-time exception: `Timeout.InfiniteTimeSpan`, any other negative duration, and any duration longer than the timer subsystem can wait (roughly 49.7 days, which includes `TimeSpan.MaxValue`) all mean *drain without any limit*; `TimeSpan.Zero` means *do not wait*; every duration in between is used as-is.
- Handlers still in flight when the budget runs out — and consumers that could not be confirmed cancelled after receiving at least one delivery — are logged as an **error**, never silently swallowed. The channel closes either way, so shutdown cannot wedge; those messages are redelivered by the broker.

```csharp
builder.Services.AddCentraRabbitMQ(options =>
{
    options.TotalShutdownDrainTimeout = TimeSpan.FromSeconds(30);
});
```
