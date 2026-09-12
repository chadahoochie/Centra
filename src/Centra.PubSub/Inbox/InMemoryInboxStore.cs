using System.Collections.Concurrent;
using Centra.PubSub.Inbox;

namespace Centra.PubSub.Inbox;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IInboxStore"/> for idempotent event deduplication.
/// </summary>
public sealed class InMemoryInboxStore : IInboxStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _processedEvents = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;

    public InMemoryInboxStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<bool> HasBeenProcessedAsync(string messageId, string consumerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerId);

        var key = $"{consumerId}:{messageId}";
        if (_processedEvents.TryGetValue(key, out var expiresAtUtc))
        {
            if (expiresAtUtc > _timeProvider.GetUtcNow())
            {
                return ValueTask.FromResult(true);
            }

            _processedEvents.TryRemove(key, out _);
        }

        return ValueTask.FromResult(false);
    }

    public ValueTask MarkProcessedAsync(string messageId, string consumerId, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerId);

        var key = $"{consumerId}:{messageId}";
        var duration = ttl ?? TimeSpan.FromDays(7);
        var expiresAtUtc = _timeProvider.GetUtcNow().Add(duration);

        _processedEvents[key] = expiresAtUtc;
        return ValueTask.CompletedTask;
    }
}
