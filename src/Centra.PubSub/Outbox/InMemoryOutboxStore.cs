using System.Collections.Concurrent;
using Centra.PubSub.Outbox;

namespace Centra.PubSub.Outbox;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IOutboxStore"/> for local execution and testing.
/// </summary>
public sealed class InMemoryOutboxStore : IOutboxStore
{
    private readonly ConcurrentDictionary<string, OutboxMessage> _messages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _pendingQueue = new();
    private readonly ConcurrentDictionary<string, string> _failures = new(StringComparer.OrdinalIgnoreCase);

    public int PendingCount => _messages.Count;

    public ValueTask EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _messages[message.Id] = message;
        _pendingQueue.Enqueue(message.Id);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var result = new List<OutboxMessage>(Math.Min(batchSize, _messages.Count));
        foreach (var msg in _messages.Values)
        {
            result.Add(msg);
            if (result.Count >= batchSize)
            {
                break;
            }
        }

        return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(result);
    }

    public ValueTask MarkPublishedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        _messages.TryRemove(messageId, out _);
        _failures.TryRemove(messageId, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask MarkFailedAsync(string messageId, string error, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        _failures[messageId] = error;
        return ValueTask.CompletedTask;
    }
}
