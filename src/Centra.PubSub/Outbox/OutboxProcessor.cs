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
    private readonly OutboxGroupDispatcher _dispatcher;

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
        _dispatcher = new OutboxGroupDispatcher(_publisher, _outboxStore, _logger);
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

        if (_options.EnableBatchPublishing)
        {
            var groups = new Dictionary<(string PubSubName, string Topic), List<OutboxMessage>>();
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                var key = (msg.PubSubName, msg.Topic);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = [];
                    groups[key] = list;
                }
                list.Add(msg);
            }

            if (_options.MaxConcurrentPublishes > 1 && groups.Count > 1)
            {
                int publishedCounter = 0;
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = _options.MaxConcurrentPublishes,
                    CancellationToken = cancellationToken
                };

                await Parallel.ForEachAsync(groups.Values, parallelOptions, async (group, ct) =>
                {
                    var count = await _dispatcher.DispatchGroupAsync(group, ct).ConfigureAwait(false);
                    Interlocked.Add(ref publishedCounter, count);
                }).ConfigureAwait(false);

                return publishedCounter;
            }

            int publishedTotal = 0;
            foreach (var group in groups.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                publishedTotal += await _dispatcher.DispatchGroupAsync(group, cancellationToken).ConfigureAwait(false);
            }

            return publishedTotal;
        }

        if (_options.MaxConcurrentPublishes > 1 && messages.Count > 1)
        {
            int publishedCounter = 0;
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.MaxConcurrentPublishes,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(messages, parallelOptions, async (message, ct) =>
            {
                if (await _dispatcher.DispatchSingleAsync(message, ct).ConfigureAwait(false))
                {
                    Interlocked.Increment(ref publishedCounter);
                }
            }).ConfigureAwait(false);

            return publishedCounter;
        }

        int publishedCount = 0;
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _dispatcher.DispatchSingleAsync(message, cancellationToken).ConfigureAwait(false))
            {
                publishedCount++;
            }
        }

        return publishedCount;
    }
}
