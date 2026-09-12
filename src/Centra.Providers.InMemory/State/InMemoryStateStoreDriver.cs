using System.Collections.Concurrent;
using Centra.Drivers;
using Centra.State;

namespace Centra.Providers.InMemory.State;

public sealed class InMemoryStateStoreDriver : IStateStoreDriver
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, InMemoryStateRecord>> _stores = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;

    public InMemoryStateStoreDriver(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<StateEntry<byte[]>?> GetAsync(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var store = _stores.GetOrAdd(storeName, static _ => new ConcurrentDictionary<string, InMemoryStateRecord>(StringComparer.OrdinalIgnoreCase));
        if (store.TryGetValue(key, out var record))
        {
            var now = _timeProvider.GetUtcNow();
            if (record.ExpiresAt.HasValue && record.ExpiresAt.Value <= now)
            {
                store.TryRemove(key, out _);
                return new ValueTask<StateEntry<byte[]>?>((StateEntry<byte[]>?)null);
            }

            var entry = new StateEntry<byte[]>(key, record.Value, record.ETag, record.Metadata);
            return new ValueTask<StateEntry<byte[]>?>((StateEntry<byte[]>?)entry);
        }

        return new ValueTask<StateEntry<byte[]>?>((StateEntry<byte[]>?)null);
    }

    public ValueTask SetAsync(
        string storeName,
        string key,
        ReadOnlyMemory<byte> value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var store = _stores.GetOrAdd(storeName, static _ => new ConcurrentDictionary<string, InMemoryStateRecord>(StringComparer.OrdinalIgnoreCase));
        var etag = Guid.NewGuid().ToString("N");
        DateTimeOffset? expiresAt = options?.TimeToLive.HasValue == true
            ? _timeProvider.GetUtcNow() + options.TimeToLive.Value
            : null;

        var record = new InMemoryStateRecord(value.ToArray(), etag, expiresAt, options?.Metadata);
        store[key] = record;

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TrySetAsync(
        string storeName,
        string key,
        ReadOnlyMemory<byte> value,
        string expectedETag,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var store = _stores.GetOrAdd(storeName, static _ => new ConcurrentDictionary<string, InMemoryStateRecord>(StringComparer.OrdinalIgnoreCase));

        if (store.TryGetValue(key, out var existing))
        {
            var now = _timeProvider.GetUtcNow();
            if (existing.ExpiresAt.HasValue && existing.ExpiresAt.Value <= now)
            {
                store.TryRemove(key, out _);
                return new ValueTask<bool>(false);
            }

            if (!string.Equals(existing.ETag, expectedETag, StringComparison.Ordinal))
            {
                return new ValueTask<bool>(false);
            }
        }
        else if (!string.IsNullOrEmpty(expectedETag))
        {
            // Expected ETag but no existing item
            return new ValueTask<bool>(false);
        }

        var newEtag = Guid.NewGuid().ToString("N");
        DateTimeOffset? expiresAt = options?.TimeToLive.HasValue == true
            ? _timeProvider.GetUtcNow() + options.TimeToLive.Value
            : null;

        var newRecord = new InMemoryStateRecord(value.ToArray(), newEtag, expiresAt, options?.Metadata);

        if (existing.Value is not null)
        {
            var updated = store.TryUpdate(key, newRecord, existing);
            return new ValueTask<bool>(updated);
        }
        else
        {
            var added = store.TryAdd(key, newRecord);
            return new ValueTask<bool>(added);
        }
    }

    public ValueTask DeleteAsync(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var store = _stores.GetOrAdd(storeName, static _ => new ConcurrentDictionary<string, InMemoryStateRecord>(StringComparer.OrdinalIgnoreCase));
        store.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TryDeleteAsync(
        string storeName,
        string key,
        string expectedETag,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var store = _stores.GetOrAdd(storeName, static _ => new ConcurrentDictionary<string, InMemoryStateRecord>(StringComparer.OrdinalIgnoreCase));
        if (store.TryGetValue(key, out var existing))
        {
            if (string.Equals(existing.ETag, expectedETag, StringComparison.Ordinal))
            {
                var removed = store.TryRemove(new KeyValuePair<string, InMemoryStateRecord>(key, existing));
                return new ValueTask<bool>(removed);
            }
        }

        return new ValueTask<bool>(false);
    }

    public ValueTask ExecuteTransactionAsync(
        string storeName,
        IReadOnlyList<StateTransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        var store = _stores.GetOrAdd(storeName, static _ => new ConcurrentDictionary<string, InMemoryStateRecord>(StringComparer.OrdinalIgnoreCase));

        lock (store)
        {
            foreach (var op in operations)
            {
                if (op is SetTransactionOperation<byte[]> setOp)
                {
                    var etag = Guid.NewGuid().ToString("N");
                    DateTimeOffset? expiresAt = setOp.Options?.TimeToLive.HasValue == true
                        ? _timeProvider.GetUtcNow() + setOp.Options.TimeToLive.Value
                        : null;

                    store[setOp.Key] = new InMemoryStateRecord(setOp.Value, etag, expiresAt, setOp.Options?.Metadata);
                }
                else if (op is DeleteTransactionOperation delOp)
                {
                    store.TryRemove(delOp.Key, out _);
                }
            }
        }

        return ValueTask.CompletedTask;
    }
}
