namespace Centra.Sample.DockerStack.Domain;

public sealed record OrderProcessingResult(
    string OrderId,
    string Status,
    string? TransactionId,
    string? TrackingNumber,
    string? Notes);
