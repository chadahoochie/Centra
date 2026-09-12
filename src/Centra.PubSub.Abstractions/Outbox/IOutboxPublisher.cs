namespace Centra.PubSub.Outbox;

/// <summary>
/// Client interface for enqueuing domain events into the transactional outbox.
/// </summary>
public interface IOutboxPublisher
{
    /// <summary>
    /// Enqueues an event into the transactional outbox to be delivered asynchronously.
    /// </summary>
    ValueTask EnqueueAsync<T>(
        string topic,
        T data,
        string? pubSubName = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a raw binary payload into the transactional outbox.
    /// </summary>
    ValueTask EnqueueRawAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        string? pubSubName = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);
}
