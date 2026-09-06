namespace Centra.Sample.OrdersService.Domain;

public sealed record CreateOrderRequest(
    string CustomerId,
    string ProductId,
    int Quantity,
    decimal UnitPrice);
