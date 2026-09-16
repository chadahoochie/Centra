namespace Centra.Sample.TenantOffload.Domain;

public sealed record CreateTenantOrderRequest(
    string TenantId,
    decimal Amount,
    string? Description = null);
