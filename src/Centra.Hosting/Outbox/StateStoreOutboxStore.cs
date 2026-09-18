using Centra.PubSub.Outbox;
using Centra.State;

namespace Centra.Hosting.Outbox;

/// <summary>
/// Persistent implementation of <see cref="IOutboxStore"/> backed by any configured <see cref="IStateStore"/>.
/// </summary>
public sealed class StateStoreOutboxStore : IOutboxStore
{
    private const string PendingIndexKey = "centra:outbox:pending";
    private readonly IStateStore _stateStore;
    private readonly string _storeName;

    public StateStoreOutboxStore(IStateStore stateStore, string storeName = "statestore")
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _storeName = storeName;
    }

    public async ValueTask EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var msgKey = $"centra:outbox:msg:{message.Id}";
        var record = new OutboxMessageRecord(
            message.Id,
            message.PubSubName,
            message.Topic,
            message.Payload.ToArray(),
            message.Headers,
            message.CreatedAtUtc);

        await _stateStore.SetAsync(_storeName, msgKey, record, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Optimistically update the pending index
        const int maxRetries = 5;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var existing = await _stateStore.GetAsync<List<string>>(_storeName, PendingIndexKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            var list = existing.HasValue ? new List<string>(existing.Value.Value) : [];
            if (!list.Contains(message.Id))
            {
                list.Add(message.Id);
            }

            var etag = existing?.ETag ?? string.Empty;
            var success = await _stateStore.TrySetAsync(_storeName, PendingIndexKey, list, etag, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (success)
            {
                break;
            }
        }
    }

    public async ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var existing = await _stateStore.GetAsync<List<string>>(_storeName, PendingIndexKey, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!existing.HasValue || existing.Value.Value.Count == 0)
        {
            return Array.Empty<OutboxMessage>();
        }

        var idsToFetch = existing.Value.Value.Take(batchSize).ToList();
        var keys = new string[idsToFetch.Count];
        for (int i = 0; i < idsToFetch.Count; i++)
        {
            keys[i] = $"centra:outbox:msg:{idsToFetch[i]}";
        }

        var batch = await _stateStore.GetBatchAsync<OutboxMessageRecord>(_storeName, keys, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (batch is null || (batch.Count == 0 && idsToFetch.Count > 0))
        {
            var fallback = new List<OutboxMessage>(idsToFetch.Count);
            foreach (var id in idsToFetch)
            {
                var msgKey = $"centra:outbox:msg:{id}";
                var entry = await _stateStore.GetAsync<OutboxMessageRecord>(_storeName, msgKey, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (entry.HasValue)
                {
                    var rec = entry.Value.Value;
                    fallback.Add(new OutboxMessage(rec.Id, rec.PubSubName, rec.Topic, rec.Payload, rec.Headers, rec.CreatedAtUtc));
                }
            }

            return fallback;
        }

        var result = new List<OutboxMessage>(batch.Count);
        for (int i = 0; i < batch.Count; i++)
        {
            var rec = batch[i].Value;
            result.Add(new OutboxMessage(rec.Id, rec.PubSubName, rec.Topic, rec.Payload, rec.Headers, rec.CreatedAtUtc));
        }

        return result;
    }

    public async ValueTask MarkPublishedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var msgKey = $"centra:outbox:msg:{messageId}";
        await _stateStore.DeleteAsync(_storeName, msgKey, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Optimistically remove from pending index
        const int maxRetries = 5;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var existing = await _stateStore.GetAsync<List<string>>(_storeName, PendingIndexKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!existing.HasValue)
            {
                break;
            }

            var list = new List<string>(existing.Value.Value);
            if (!list.Remove(messageId))
            {
                break;
            }

            var etag = existing.Value.ETag;
            var success = await _stateStore.TrySetAsync(_storeName, PendingIndexKey, list, etag, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (success)
            {
                break;
            }
        }
    }

    public async ValueTask MarkFailedAsync(string messageId, string error, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var failKey = $"centra:outbox:fail:{messageId}";
        await _stateStore.SetAsync(_storeName, failKey, error, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
