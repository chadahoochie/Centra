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
        var pubSubTopics = _topics.GetOrAdd(pubSubName, static _ => new ConcurrentDictionary<string, ConcurrentBag<InMemorySubscription>>(StringComparer.OrdinalIgnoreCase));
        if (!pubSubTopics.TryGetValue(topic, out var subscriptions) || subscriptions.IsEmpty)
        {
            return;
        }

        // Competing-consumer subscribers all get every message (current/legacy behavior);
        // single-active subscribers share one message per publish, round-robin.
        var competing = subscriptions.Where(s => s.ConsumerMode == ConsumerMode.CompetingConsumer);
        foreach (var sub in competing)
        {
            await sub.DispatchAsync(this, pubSubName, payload, metadata, cancellationToken).ConfigureAwait(false);
        }

        var singleActive = subscriptions.Where(s => s.ConsumerMode == ConsumerMode.SingleActiveConsumer).ToList();
        if (singleActive.Count > 0)
        {
            var subKey = $"{pubSubName}:{topic}";
            var index = _singleActiveRoundRobinIndex.AddOrUpdate(subKey, 0, (_, current) => (current + 1) % singleActive.Count);
            var chosen = singleActive[Math.Abs(index) % singleActive.Count];
            await chosen.DispatchAsync(this, pubSubName, payload, metadata, cancellationToken).ConfigureAwait(false);
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
        var pubSubTopics = _topics.GetOrAdd(pubSubName, static _ => new ConcurrentDictionary<string, ConcurrentBag<InMemorySubscription>>(StringComparer.OrdinalIgnoreCase));
        var subs = pubSubTopics.GetOrAdd(topic, static _ => new ConcurrentBag<InMemorySubscription>());

        subs.Add(new InMemorySubscription(handler, deadLetterTopic, options?.ConsumerMode ?? ConsumerMode.CompetingConsumer, options));
        return ValueTask.CompletedTask;
    }

    public ValueTask UnsubscribeAsync(
        string pubSubName,
        string topic,
        CancellationToken cancellationToken = default)
    {
        var pubSubTopics = _topics.GetOrAdd(pubSubName, static _ => new ConcurrentDictionary<string, ConcurrentBag<InMemorySubscription>>(StringComparer.OrdinalIgnoreCase));
        pubSubTopics.TryRemove(topic, out _);
        return ValueTask.CompletedTask;
    }
}
