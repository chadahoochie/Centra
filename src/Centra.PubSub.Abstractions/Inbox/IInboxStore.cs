namespace Centra.PubSub.Inbox;

/// <summary>
/// Defines the storage contract for idempotent inbox message deduplication.
/// </summary>
public interface IInboxStore
{
    /// <summary>
    /// Checks whether an event with the given message identifier has already been processed by the specified consumer.
    /// </summary>
    ValueTask<bool> HasBeenProcessedAsync(string messageId, string consumerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that an event has been successfully processed by the specified consumer.
    /// </summary>
    ValueTask MarkProcessedAsync(string messageId, string consumerId, TimeSpan? ttl = null, CancellationToken cancellationToken = default);
}
