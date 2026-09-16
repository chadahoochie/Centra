namespace Centra.Sample.TenantOffload.Domain;

public readonly record struct ProcessedOrderInfo(
    DateTimeOffset ProcessedAt,
    string OrderId,
    string TenantId,
    decimal Amount,
    bool WasOffloaded,
    string InstanceId);
