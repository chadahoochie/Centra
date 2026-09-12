namespace Centra.PubSub.Outbox;

/// <summary>
/// Defines the storage contract for transactional outbox persistence and dispatching.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Persists an outbox message to storage.
    /// </summary>
    ValueTask EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches pending outbox messages ready for delivery.
    /// </summary>
    ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an outbox message as successfully published to the message broker.
    /// </summary>
    ValueTask MarkPublishedAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an outbox message as failed with error details.
    /// </summary>
    ValueTask MarkFailedAsync(string messageId, string error, CancellationToken cancellationToken = default);
}
