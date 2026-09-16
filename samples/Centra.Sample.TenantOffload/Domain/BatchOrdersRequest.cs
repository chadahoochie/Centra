namespace Centra.Sample.TenantOffload.Domain;

public sealed record BatchOrdersRequest(
    int NoisyCount = 60,
    int HonestCount = 10);
