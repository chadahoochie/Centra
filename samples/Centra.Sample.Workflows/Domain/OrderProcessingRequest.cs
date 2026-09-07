namespace Centra.Sample.Workflows.Domain;

/// <summary>
/// Domain request model to initiate an order processing workflow.
/// </summary>
public sealed record OrderProcessingRequest(
    string OrderId,
    string CustomerId,
    string ProductId,
    int Quantity,
    decimal TotalAmount);
