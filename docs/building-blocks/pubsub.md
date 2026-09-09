# Pub/Sub Messaging & CNCF CloudEvents v1.0

> Publish and subscribe to events across microservices using CNCF CloudEvents v1.0, consumer groups, dead lettering, and ambient W3C distributed tracing context.

---

## 🎯 Key Concepts

- **`IPubSubClient`**: Unified publishing client wrapping payloads into CloudEvents.
- **`IEventHandler<T>`**: Strongly typed consumer interface.
- **`[Topic]` Attribute**: Declarative metadata associating handlers with pub/sub component names, topics, dead-letter targets, and consumer modes.
- **CNCF CloudEvents v1.0**: Standardized envelopes supporting both **Binary Mode** (default zero-alloc) and **Structured Mode**.

---

## 🛠️ Registration

Centra supports two ways to register pub/sub event handlers:

### Option A: Declarative via `[Topic]` Attribute (Automatic Discovery)
Decorate your handler class with `[Topic]` and call `builder.Services.AddCentra()` in `Program.cs`. Centra's assembly scanner automatically discovers and registers all decorated handlers:

```csharp
// 1. Add Centra and scan entry assembly
builder.Services.AddCentra();

// 2. Add provider (e.g. RabbitMQ, Redis, Azure Service Bus, or InMemory)
builder.Services.AddCentraRabbitMQ(options =>
{
    options.HostName = "localhost";
    options.ExchangeName = "centra.events";
});
```

### Option B: Programmatic via `AddCentraEventHandler` Extension (Explicit & Dynamic Overrides)
Use `AddCentraEventHandler<THandler, TEvent>()` to explicitly register a handler or dynamically configure topic names, pub/sub brokers, dead-letter topics, or consumer modes from runtime settings:

```csharp
// 1. Add Pub/Sub building blocks (modular registration adhering to ISP)
builder.Services.AddCentraPubSub();

// 2. Add provider
builder.Services.AddCentraRabbitMQ(options =>
{
    options.HostName = "localhost";
    options.ExchangeName = "centra.events";
});

// 3. Explicit programmatic registration with runtime parameters
builder.Services.AddCentraEventHandler<PaymentNotificationHandler, OrderCreatedEvent>(
    pubSubName: builder.Configuration["PubSub:ComponentName"] ?? "pubsub",
    topic: builder.Configuration["PubSub:OrdersTopic"] ?? "orders.created",
    deadLetterTopic: "orders.created.dlq",
    consumerMode: ConsumerMode.CompetingConsumer);
```

> [!TIP]
> **Precedence & Fallback Rule**: Explicit parameters passed to `AddCentraEventHandler` take precedence and override values declared on the `[Topic]` attribute. Any argument left as `null` falls back to the `[Topic]` attribute on the handler, and if omitted there, to framework defaults.

---

## 📤 Publishing Events

Inject `IPubSubClient` and publish your domain event record:

```csharp
app.MapPost("/checkout", async (CheckoutRequest request, IPubSubClient pubSub) =>
{
    var @event = new OrderCreatedEvent(
        OrderId: $"ord-{Guid.NewGuid():N}"[..12],
        CustomerId: request.CustomerId,
        Amount: request.TotalAmount);

    // Publishes to default pubsub component and "orders.created" topic
    await pubSub.PublishAsync("orders.created", @event);

    return Results.Accepted();
});
```

### Specifying Options & Metadata
You can customize the CloudEvent mode and add enterprise extensions:

```csharp
var options = new PubSubPublishOptions(
    PubSubName: "custom-bus",
    Mode: CloudEventMode.Binary, // Raw bytes body + ce-* headers (fastest)
    Metadata: new Dictionary<string, string>
    {
        ["ce-tenantid"] = "tenant-emea-42",
        ["ce-correlationid"] = correlationId
    });

await pubSub.PublishAsync("orders.created", @event, options);
```

---

## 📥 Subscribing with Pure Handlers

Implement `IEventHandler<T>` and decorate your class with `[Topic]`:

```csharp
using Centra.Events;
using Centra.PubSub;

[Topic(
    PubSubName = "pubsub", 
    Topic = "orders.created", 
    DeadLetterTopic = "orders.created.dlq",
    ConsumerMode = ConsumerMode.CompetingConsumer)]
public sealed class PaymentNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<PaymentNotificationHandler> _logger;

    public PaymentNotificationHandler(ILogger<PaymentNotificationHandler> logger)
    {
        _logger = logger;
    }

    public async Task<EventHandlingResult> HandleAsync(
        OrderCreatedEvent @event, 
        EventContext context, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing event {EventId} for order {OrderId}. Source: {Source}",
            context.Id, @event.OrderId, context.Source);

        try
        {
            await ProcessPaymentAsync(@event, cancellationToken);
            return EventHandlingResult.Success;
        }
        catch (TransientGatewayException)
        {
            // Requests redelivery / retry from the message broker
            return EventHandlingResult.Retry;
        }
        catch (InvalidOrderException)
        {
            // Moves event directly to DeadLetterTopic
            return EventHandlingResult.DeadLetter;
        }
    }
}
```

### Event Handling Return Statuses:
- **`EventHandlingResult.Success`**: Acknowledges message receipt (`Complete` / `Ack`).
- **`EventHandlingResult.Retry`**: Rejects and returns message to broker for redelivery (`Nack` / `Abandon`).
- **`EventHandlingResult.Drop`**: Silently drops message without retry.
- **`EventHandlingResult.DeadLetter`**: Routes message to dead-letter queue / topic.

---

## 🔗 Ambient W3C TraceContext Propagation

When an event is published, Centra injects the current W3C `traceparent` and `tracestate` into the CloudEvent headers or AMQP properties.

When the subscriber handler runs, Centra automatically:
1. Extracts `traceparent` and `tracestate`.
2. Starts an OpenTelemetry activity (`ActivityKind.Consumer`) with the publisher as parent.
3. Propagates the ambient context into your ASP.NET Core `HttpContext` and downstream service calls.
