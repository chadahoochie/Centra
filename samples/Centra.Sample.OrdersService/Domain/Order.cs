namespace Centra.Sample.OrdersService.Domain;

public sealed record Order(
    string Id,
    string CustomerId,
    string ProductId,
    int Quantity,
    decimal TotalAmount,
    string Status);
