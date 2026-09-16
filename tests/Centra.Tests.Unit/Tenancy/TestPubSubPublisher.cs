using Centra.PubSub;

namespace Centra.Tests.Unit.Tenancy;

public sealed class TestPubSubPublisher : IPubSubPublisher
{
    public List<(string PubSubName, string Topic, ReadOnlyMemory<byte> Payload, IReadOnlyDictionary<string, string> Metadata)> PublishedMessages { get; } = new();

    public ValueTask PublishAsync(
        string pubSubName,
        string topic,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        PublishedMessages.Add((pubSubName, topic, payload, metadata));
        return ValueTask.CompletedTask;
    }

    public ValueTask PublishBatchAsync(
        string pubSubName,
        string topic,
        IReadOnlyList<PubSubMessage> messages,
        CancellationToken cancellationToken = default)
    {
        foreach (var msg in messages)
        {
            PublishedMessages.Add((pubSubName, topic, msg.Payload, msg.Metadata ?? new Dictionary<string, string>()));
        }
        return ValueTask.CompletedTask;
    }
}
