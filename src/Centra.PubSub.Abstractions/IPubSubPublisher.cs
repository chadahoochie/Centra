namespace Centra.PubSub;

public interface IPubSubPublisher
{
    ValueTask PublishAsync(
        string pubSubName,
        string topic,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    ValueTask PublishBatchAsync(
        string pubSubName,
        string topic,
        IReadOnlyList<PubSubMessage> messages,
        CancellationToken cancellationToken = default);
}
