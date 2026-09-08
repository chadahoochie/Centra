namespace Centra.Sample.DockerStack.Domain;

public sealed record OrderProcessingRequest(
    string OrderId,
    string CustomerId,
    string ProductId,
    int Quantity,
    decimal TotalAmount);
