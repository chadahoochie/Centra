using Centra.Events;

namespace Centra.Sample.OrdersService.Domain;

[EventContract("orders.created", Version = "1")]
public sealed record OrderCreatedEvent(
    string OrderId,
    string CustomerId,
    string ProductId,
    decimal TotalAmount);
