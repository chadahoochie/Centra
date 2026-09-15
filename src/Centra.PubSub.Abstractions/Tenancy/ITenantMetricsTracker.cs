namespace Centra.PubSub.Tenancy;

public interface ITenantMetricsTracker
{
    void RecordExecution(string tenantId, string topic, double durationMs);

    TenantTrafficStats GetTenantStats(string tenantId, string topic);

    IReadOnlyList<TenantTrafficStats> GetAllTenantStats(string topic);

    bool ShouldOffload(string tenantId, string topic, out TenantOffloadReason reason);

    void Reset(string? tenantId = null);
}
