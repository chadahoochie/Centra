namespace Centra.Sample.Workflows.Domain;

/// <summary>
/// Domain result returned upon order processing workflow completion.
/// </summary>
public sealed record OrderProcessingResult(
    string OrderId,
    string Status,
    string? TransactionId,
    string? TrackingNumber,
    string? Notes);
