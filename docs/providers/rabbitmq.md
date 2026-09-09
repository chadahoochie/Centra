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
