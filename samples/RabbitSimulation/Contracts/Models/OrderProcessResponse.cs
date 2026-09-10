namespace Centra.Sample.RabbitSimulation.Contracts.Models;

public sealed record OrderProcessResponse(
    string OrderId,
    string Status,
    string AuthorizationCode,
    decimal FinalAmount,
    DateTimeOffset ProcessedAt,
    string Message);
