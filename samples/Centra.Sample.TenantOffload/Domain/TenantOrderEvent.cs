namespace Centra.Sample.TenantOffload.Domain;

public sealed record TenantOrderEvent(
    string OrderId,
    string TenantId,
    decimal Amount,
    string Description);
