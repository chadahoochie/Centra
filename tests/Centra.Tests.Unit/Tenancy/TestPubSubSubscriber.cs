using Centra.PubSub;

namespace Centra.Tests.Unit.Tenancy;

public sealed class TestPubSubSubscriber : IPubSubSubscriber
{
    public List<(string PubSubName, string Topic, Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> Handler)> Subscriptions { get; } = new();
    public List<(string PubSubName, string Topic)> Unsubscriptions { get; } = new();

    public ValueTask SubscribeAsync(
        string pubSubName,
        string topic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        string? deadLetterTopic = null,
        CancellationToken cancellationToken = default,
        PubSubSubscribeOptions? options = null)
    {
        Subscriptions.Add((pubSubName, topic, handler));
        return ValueTask.CompletedTask;
    }

    public ValueTask UnsubscribeAsync(
        string pubSubName,
        string topic,
        CancellationToken cancellationToken = default)
    {
        Unsubscriptions.Add((pubSubName, topic));
        return ValueTask.CompletedTask;
    }
}
