using System.Collections.Concurrent;

namespace Centra.Sample.TenantOffload.Domain;

public sealed class TenantConsumerNodeState : ITenantConsumerNodeState
{
    private readonly ConcurrentDictionary<string, int> _tenantCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<ProcessedOrderInfo> _recentOrders = new();
    private const int MaxRecentOrders = 50;
    private int _totalHandled;

    public string InstanceId { get; }

    public string BrokerType { get; }

    public int TotalHandled => Volatile.Read(ref _totalHandled);

    public IReadOnlyDictionary<string, int> TenantHandledCounts => _tenantCounts;

    public IReadOnlyList<ProcessedOrderInfo> RecentProcessedOrders => _recentOrders.ToArray();

    public TenantConsumerNodeState(string instanceId, string brokerType)
    {
        InstanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        BrokerType = brokerType ?? throw new ArgumentNullException(nameof(brokerType));
    }

    public void RecordHandled(string tenantId, string orderId, decimal amount, bool wasOffloaded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        Interlocked.Increment(ref _totalHandled);
        _tenantCounts.AddOrUpdate(tenantId, 1, static (_, count) => count + 1);

        _recentOrders.Enqueue(new ProcessedOrderInfo(
            DateTimeOffset.UtcNow,
            orderId,
            tenantId,
            amount,
            wasOffloaded,
            InstanceId));

        while (_recentOrders.Count > MaxRecentOrders && _recentOrders.TryDequeue(out _))
        {
        }
    }
}
