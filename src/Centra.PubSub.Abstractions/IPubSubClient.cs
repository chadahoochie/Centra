namespace Centra.PubSub;

public interface IPubSubClient
{
    ValueTask PublishAsync<T>(
        string topic,
        T data,
        PubSubPublishOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask PublishAsync<T>(
        string pubSubName,
        string topic,
        T data,
        PubSubPublishOptions? options = null,
        CancellationToken cancellationToken = default);
}
