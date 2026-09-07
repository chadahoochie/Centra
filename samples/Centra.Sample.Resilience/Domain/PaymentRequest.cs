namespace Centra.Sample.Resilience.Domain;

public sealed record PaymentRequest(
    string OrderId,
    decimal Amount,
    string Currency);
