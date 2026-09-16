namespace Centra.Sample.TenantOffload.Domain;

public interface ITenantConsumerNodeState
{
    string InstanceId { get; }

    string BrokerType { get; }

    int TotalHandled { get; }

    IReadOnlyDictionary<string, int> TenantHandledCounts { get; }

    IReadOnlyList<ProcessedOrderInfo> RecentProcessedOrders { get; }

    void RecordHandled(string tenantId, string orderId, decimal amount, bool wasOffloaded);
}
