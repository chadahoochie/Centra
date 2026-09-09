using System.Collections.Concurrent;
using Centra.Drivers;
using Centra.PubSub;

namespace Centra.Providers.InMemory.PubSub;

public sealed class InMemoryPubSubDriver : IPubSubDriver
{
    // pubSubName -> (topic -> list of subscriptions)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<InMemorySubscription>>> _topics =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _singleActiveRoundRobinIndex = new(StringComparer.OrdinalIgnoreCase);

    public async ValueTask PublishAsync(
        string pubSubName,
        string topic,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        var pubSubTopics = GetOrCreatePubSub(pubSubName);
        if (!pubSubTopics.TryGetValue(topic, out var subscriptions) || subscriptions.IsEmpty)
        {
            return;
        }

        // Competing-consumer subscribers all get every message (current/legacy behavior);
        // single-active subscribers share one message per publish, round-robin.
        var competing = subscriptions.Where(s => s.ConsumerMode == ConsumerMode.CompetingConsumer);
        foreach (var sub in competing)
        {
            await DispatchAsync(pubSubName, sub, payload, metadata, cancellationToken).ConfigureAwait(false);
        }

        var singleActive = subscriptions.Where(s => s.ConsumerMode == ConsumerMode.SingleActiveConsumer).ToList();
        if (singleActive.Count > 0)
        {
            var subKey = $"{pubSubName}:{topic}";
            var index = _singleActiveRoundRobinIndex.AddOrUpdate(subKey, 0, (_, current) => (current + 1) % singleActive.Count);
            var chosen = singleActive[Math.Abs(index) % singleActive.Count];
            await DispatchAsync(pubSubName, chosen, payload, metadata, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask DispatchAsync(
        string pubSubName,
        InMemorySubscription sub,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await sub.Handler(payload, metadata, cancellationToken).ConfigureAwait(false);
            if (result == EventHandlingResult.DeadLetter && !string.IsNullOrWhiteSpace(sub.DeadLetterTopic))
            {
                await PublishAsync(pubSubName, sub.DeadLetterTopic, payload, metadata, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(sub.DeadLetterTopic))
            {
                await PublishAsync(pubSubName, sub.DeadLetterTopic, payload, metadata, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw;
            }
        }
    }

    public ValueTask SubscribeAsync(
        string pubSubName,
        string topic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        string? deadLetterTopic = null,
        CancellationToken cancellationToken = default,
        PubSubSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var pubSubTopics = GetOrCreatePubSub(pubSubName);
        var subs = pubSubTopics.GetOrAdd(topic, static _ => new ConcurrentBag<InMemorySubscription>());

        subs.Add(new InMemorySubscription(handler, deadLetterTopic, options?.ConsumerMode ?? ConsumerMode.CompetingConsumer));
        return ValueTask.CompletedTask;
    }

    public ValueTask UnsubscribeAsync(
        string pubSubName,
        string topic,
        CancellationToken cancellationToken = default)
    {
        var pubSubTopics = GetOrCreatePubSub(pubSubName);
        pubSubTopics.TryRemove(topic, out _);
        return ValueTask.CompletedTask;
    }

    private ConcurrentDictionary<string, ConcurrentBag<InMemorySubscription>> GetOrCreatePubSub(string pubSubName)
    {
        return _topics.GetOrAdd(pubSubName, static _ => new ConcurrentDictionary<string, ConcurrentBag<InMemorySubscription>>(StringComparer.OrdinalIgnoreCase));
    }
}
