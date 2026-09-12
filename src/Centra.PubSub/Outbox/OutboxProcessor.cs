using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Centra.PubSub.Outbox;

/// <summary>
/// Background processor draining pending messages from <see cref="IOutboxStore"/> and publishing them
/// through the configured <see cref="IPubSubPublisher"/>.
/// </summary>
public sealed class OutboxProcessor
{
    private readonly IOutboxStore _outboxStore;
    private readonly IPubSubPublisher _publisher;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        IOutboxStore outboxStore,
        IPubSubPublisher publisher,
        OutboxOptions? options = null,
        ILogger<OutboxProcessor>? logger = null)
    {
        _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _options = options ?? new OutboxOptions();
        _logger = logger ?? NullLogger<OutboxProcessor>.Instance;
    }

    /// <summary>
    /// Executes a single sweep of the outbox, publishing pending messages in batches.
    /// Returns the number of messages successfully published.
    /// </summary>
    public async ValueTask<int> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        var messages = await _outboxStore.FetchPendingAsync(_options.BatchSize, cancellationToken).ConfigureAwait(false);
        if (messages.Count == 0)
        {
            return 0;
        }

        int publishedCount = 0;
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _publisher.PublishAsync(
                    message.PubSubName,
                    message.Topic,
                    message.Payload,
                    message.Headers,
                    cancellationToken).ConfigureAwait(false);

                await _outboxStore.MarkPublishedAsync(message.Id, cancellationToken).ConfigureAwait(false);
                publishedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish outbox message '{MessageId}' to topic '{Topic}' on pubsub '{PubSubName}'",
                    message.Id, message.Topic, message.PubSubName);

                await _outboxStore.MarkFailedAsync(message.Id, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }

        return publishedCount;
    }
}
