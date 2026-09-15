using Centra.PubSub;

namespace Centra.PubSub.Tenancy;

public sealed class BoundedShardBrokerTopicOffloadStrategy : ITenantOffloadStrategy
{
    private readonly IPubSubPublisher _publisher;
    private readonly TenantOffloadOptions _options;

    public TenantOffloadStrategyType StrategyType => TenantOffloadStrategyType.BoundedShardBrokerTopic;

    public BoundedShardBrokerTopicOffloadStrategy(
        IPubSubPublisher publisher,
        TenantOffloadOptions? options = null)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _options = options ?? new TenantOffloadOptions();
    }

    public async ValueTask<EventHandlingResult> ExecuteOffloadAsync(TenantOffloadWorkItem workItem, CancellationToken cancellationToken)
    {
        var shardCount = Math.Max(1, _options.OffloadShardCount);
        var shardId = TenantDeterministicHash.GetShardId(workItem.TenantId, shardCount);
        var targetTopic = $"{workItem.Topic}.offload.{shardId}";

        var headers = new Dictionary<string, string>(workItem.Headers, StringComparer.OrdinalIgnoreCase)
        {
            ["ce-offloaded"] = "true",
            ["ce-offload-shard"] = shardId.ToString()
        };

        await _publisher.PublishAsync(
            workItem.PubSubName,
            targetTopic,
            workItem.Payload,
            headers,
            cancellationToken).ConfigureAwait(false);

        return EventHandlingResult.Success;
    }

    public string ResolvePublishTopic(string baseTopic, string tenantId)
    {
        var shardCount = Math.Max(1, _options.OffloadShardCount);
        var shardId = TenantDeterministicHash.GetShardId(tenantId, shardCount);
        return $"{baseTopic}.offload.{shardId}";
    }

    public ValueTask CleanupIdleResourcesAsync(CancellationToken cancellationToken)
    {
        // Bounded shards are static / fixed pool; no dynamic cleanup needed
        return ValueTask.CompletedTask;
    }
}
