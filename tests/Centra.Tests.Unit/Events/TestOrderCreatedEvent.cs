using Centra.Events;

namespace Centra.Tests.Unit.Events;

[EventContract("orders.created", Version = "1")]
public sealed record TestOrderCreatedEvent(string OrderId, string ProductId, int Quantity);
