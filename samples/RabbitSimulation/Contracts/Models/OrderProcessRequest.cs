namespace Centra.Sample.RabbitSimulation.Contracts.Models;

public sealed record OrderProcessRequest(
    string OrderId,
    string CustomerName,
    string ItemDescription,
    int Quantity,
    decimal UnitPrice,
    decimal TotalAmount,
    DateTimeOffset SubmittedAt);
