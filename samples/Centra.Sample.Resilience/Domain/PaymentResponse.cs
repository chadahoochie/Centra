namespace Centra.Sample.Resilience.Domain;

public sealed record PaymentResponse(
    string PaymentId,
    string OrderId,
    string Status,
    string Message);
