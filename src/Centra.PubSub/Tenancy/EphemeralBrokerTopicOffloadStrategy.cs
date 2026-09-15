using Centra.PubSub;

namespace Centra.PubSub.Tenancy;

public sealed class EphemeralBrokerTopicOffloadStrategy : ITenantOffloadStrategy
{
    private readonly IPubSubPublisher _publisher;
    private readonly IPubSubSubscriber? _subscriber;
    private readonly TenantOffloadOptions _options;
    private readonly HashSet<(string PubSubName, string Topic)> _activeOffloadTopics = new();
    private readonly object _lock = new();

    public TenantOffloadStrategyType StrategyType => TenantOffloadStrategyType.EphemeralBrokerTopic;

    public EphemeralBrokerTopicOffloadStrategy(
        IPubSubPublisher publisher,
        IPubSubSubscriber? subscriber = null,
        TenantOffloadOptions? options = null)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _subscriber = subscriber;
        _options = options ?? new TenantOffloadOptions();
    }

    public async ValueTask<EventHandlingResult> ExecuteOffloadAsync(TenantOffloadWorkItem workItem, CancellationToken cancellationToken)
    {
        var targetTopic = ResolveTopic(workItem.Topic, workItem.TenantId);

        var isNewTopic = false;
        lock (_lock)
        {
            isNewTopic = _activeOffloadTopics.Add((workItem.PubSubName, targetTopic));
        }

        if (isNewTopic && _subscriber is not null)
        {
            await _subscriber.SubscribeAsync(
                workItem.PubSubName,
                targetTopic,
                async (payload, headers, ct) =>
                {
                    return await workItem.HandlerInvoker(ct).ConfigureAwait(false);
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var headers = new Dictionary<string, string>(workItem.Headers, StringComparer.OrdinalIgnoreCase)
        {
            ["ce-offloaded"] = "true"
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
        return ResolveTopic(baseTopic, tenantId);
    }

    public async ValueTask CleanupIdleResourcesAsync(CancellationToken cancellationToken)
    {
        if (_subscriber is null)
        {
            return;
        }

        (string PubSubName, string Topic)[] topicsToClean;
        lock (_lock)
        {
            topicsToClean = _activeOffloadTopics.ToArray();
            _activeOffloadTopics.Clear();
        }

        foreach (var (pubSubName, topic) in topicsToClean)
        {
            try
            {
                await _subscriber.UnsubscribeAsync(pubSubName, topic, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Suppress broker unsubscribe errors during cleanup
            }
        }
    }

    internal string ResolveTopic(string baseTopic, string tenantId)
    {
        var pattern = _options.OffloadTopicPattern;
        return pattern
            .Replace("{topic}", baseTopic, StringComparison.Ordinal)
            .Replace("{tenantId}", tenantId, StringComparison.Ordinal);
    }
}
