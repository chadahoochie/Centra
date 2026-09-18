using Microsoft.Extensions.Logging;

namespace Centra.PubSub.Outbox;

/// <summary>
/// Dispatches single or batched outbox messages through the publisher and updates store status.
/// </summary>
internal sealed class OutboxGroupDispatcher
{
    private readonly IPubSubPublisher _publisher;
    private readonly IOutboxStore _outboxStore;
    private readonly ILogger _logger;

    public OutboxGroupDispatcher(
        IPubSubPublisher publisher,
        IOutboxStore outboxStore,
        ILogger logger)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<bool> DispatchSingleAsync(
        OutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _publisher.PublishAsync(
                message.PubSubName,
                message.Topic,
                message.Payload,
                message.Headers,
                cancellationToken).ConfigureAwait(false);

            await _outboxStore.MarkPublishedAsync(message.Id, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish outbox message '{MessageId}' to topic '{Topic}' on pubsub '{PubSubName}'",
                message.Id, message.Topic, message.PubSubName);

            await _outboxStore.MarkFailedAsync(message.Id, ex.Message, cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    public async ValueTask<int> DispatchGroupAsync(
        List<OutboxMessage> group,
        CancellationToken cancellationToken = default)
    {
        if (group.Count == 0) return 0;

        if (group.Count == 1)
        {
            return await DispatchSingleAsync(group[0], cancellationToken).ConfigureAwait(false) ? 1 : 0;
        }

        var batch = new PubSubMessage[group.Count];
        for (int i = 0; i < group.Count; i++)
        {
            batch[i] = new PubSubMessage(group[i].Payload, group[i].Headers);
        }

        try
        {
            await _publisher.PublishBatchAsync(
                group[0].PubSubName,
                group[0].Topic,
                batch,
                cancellationToken).ConfigureAwait(false);

            for (int i = 0; i < group.Count; i++)
            {
                await _outboxStore.MarkPublishedAsync(group[i].Id, cancellationToken).ConfigureAwait(false);
            }

            return group.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish batch of {Count} outbox messages to topic '{Topic}' on pubsub '{PubSubName}'",
                group.Count, group[0].Topic, group[0].PubSubName);

            for (int i = 0; i < group.Count; i++)
            {
                await _outboxStore.MarkFailedAsync(group[i].Id, ex.Message, cancellationToken).ConfigureAwait(false);
            }

            return 0;
        }
    }
}
