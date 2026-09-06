using Centra.Events;

namespace Centra.Tests.Integration.Workflows;

[EventContract("orders.created", Version = "1")]
public sealed record IntegrationOrderCreatedEvent(
    string OrderId,
    string ProductId,
    int Quantity);
