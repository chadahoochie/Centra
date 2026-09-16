using System.Collections.Concurrent;
using Centra.PubSub;

namespace Centra.PubSub.Tenancy;

public sealed class EphemeralBrokerTopicOffloadStrategy : ITenantOffloadStrategy
{
    private readonly IPubSubPublisher _publisher;
    private readonly IPubSubSubscriber? _subscriber;
    private readonly TenantOffloadOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<(string PubSubName, string Topic), Task> _topicSubscriptions = new();
    private readonly ConcurrentDictionary<(string PubSubName, string Topic), DateTimeOffset> _lastActivityTimes = new();
    private readonly ConcurrentDictionary<(string PubSubName, string Topic), Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>>> _topicHandlers = new();

    public TenantOffloadStrategyType StrategyType => TenantOffloadStrategyType.EphemeralBrokerTopic;

    public IReadOnlyCollection<(string PubSubName, string Topic)> ActiveOffloadTopics => _topicSubscriptions.Keys.ToArray();

    public EphemeralBrokerTopicOffloadStrategy(
        IPubSubPublisher publisher,
        IPubSubSubscriber? subscriber = null,
        TenantOffloadOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _subscriber = subscriber;
        _options = options ?? new TenantOffloadOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<EventHandlingResult> ExecuteOffloadAsync(TenantOffloadWorkItem workItem, CancellationToken cancellationToken)
    {
        var targetTopic = ResolveTopic(workItem.Topic, workItem.TenantId);
        var topicKey = (workItem.PubSubName, targetTopic);
        _lastActivityTimes[topicKey] = _timeProvider.GetUtcNow();

        if (workItem.DynamicInvoker is not null)
        {
            _topicHandlers[topicKey] = workItem.DynamicInvoker;
        }
        else if (workItem.HandlerInvoker is not null)
        {
            _topicHandlers[topicKey] = (p, h, ct) => workItem.HandlerInvoker(ct);
        }

        if (_subscriber is not null)
        {
            var invoker = workItem.DynamicInvoker;
            var fallback = workItem.HandlerInvoker;
            var subOptions = new PubSubSubscribeOptions
            {
                AutoDelete = true,
                MaxConcurrentCalls = _options.MaxConcurrencyPerTenant > 0 ? _options.MaxConcurrencyPerTenant : null
            };

            var subscriptionTask = _topicSubscriptions.GetOrAdd(topicKey, key =>
            {
                return _subscriber.SubscribeAsync(
                    key.PubSubName,
                    key.Topic,
                    async (payload, headers, ct) =>
                    {
                        _lastActivityTimes[key] = _timeProvider.GetUtcNow();
                        if (_topicHandlers.TryGetValue(key, out var currentHandler))
                        {
                            return await currentHandler(payload, headers, ct).ConfigureAwait(false);
                        }
                        if (invoker is not null)
                        {
                            return await invoker(payload, headers, ct).ConfigureAwait(false);
                        }
                        if (fallback is not null)
                        {
                            return await fallback(ct).ConfigureAwait(false);
                        }
                        return EventHandlingResult.Success;
                    },
                    cancellationToken: cancellationToken,
                    options: subOptions).AsTask();
            });

            try
            {
                await subscriptionTask.ConfigureAwait(false);
            }
            catch
            {
                _topicSubscriptions.TryRemove(topicKey, out _);
                throw;
            }
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

    public async ValueTask EnsureSubscribedAsync(
        string pubSubName,
        string targetTopic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        CancellationToken cancellationToken = default)
    {
        if (_subscriber is null)
        {
            return;
        }

        var key = (pubSubName, targetTopic);
        var subOptions = new PubSubSubscribeOptions
        {
            AutoDelete = true,
            MaxConcurrentCalls = _options.MaxConcurrencyPerTenant > 0 ? _options.MaxConcurrencyPerTenant : null
        };
        var task = _topicSubscriptions.GetOrAdd(key, k => _subscriber.SubscribeAsync(
            k.PubSubName,
            k.Topic,
            handler,
            cancellationToken: cancellationToken,
            options: subOptions).AsTask());
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            _topicSubscriptions.TryRemove(key, out _);
            throw;
        }
    }

    public string ResolvePublishTopic(string baseTopic, string tenantId)
    {
        return ResolveTopic(baseTopic, tenantId);
    }

    public void RecordActivity(string pubSubName, string topic)
    {
        _lastActivityTimes[(pubSubName, topic)] = _timeProvider.GetUtcNow();
    }

    public async ValueTask CleanupIdleResourcesAsync(CancellationToken cancellationToken)
    {
        if (_subscriber is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var idleTimeout = _options.LaneIdleTimeout > TimeSpan.Zero ? _options.LaneIdleTimeout : TimeSpan.FromSeconds(30);

        foreach (var kvp in _lastActivityTimes)
        {
            if (now - kvp.Value >= idleTimeout)
            {
                if (_lastActivityTimes.TryRemove(kvp.Key, out _) &&
                    _topicSubscriptions.TryRemove(kvp.Key, out _))
                {
                    _topicHandlers.TryRemove(kvp.Key, out _);
                    try
                    {
                        await _subscriber.UnsubscribeAsync(kvp.Key.PubSubName, kvp.Key.Topic, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceWarning("Failed to unsubscribe idle ephemeral topic: {0}", ex.Message);
                    }
                }
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
