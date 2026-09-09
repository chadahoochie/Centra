using Centra.Actors;
using Centra.State;

namespace Centra.Core.Actors;

/// <summary>
/// Manages persistent actor state with in-memory caching, dirty tracking, and optimistic concurrency (ETags).
/// </summary>
public sealed class ActorStateManager : IActorStateManager
{
    private readonly ActorIdentity _identity;
    private readonly IStateStore _stateStore;
    private readonly string _storeName;
    private readonly IActorStateKeyFormatter _keyFormatter;
    private readonly IActorStatePersister _persister;
    private readonly Dictionary<string, ActorStateEntry> _cache = new(StringComparer.Ordinal);

    public ActorStateManager(
        ActorIdentity identity,
        IStateStore stateStore,
        string storeName,
        IActorStateKeyFormatter? keyFormatter = null)
        : this(identity, stateStore, storeName, keyFormatter, null)
    {
    }

    internal ActorStateManager(
        ActorIdentity identity,
        IStateStore stateStore,
        string storeName,
        IActorStateKeyFormatter? keyFormatter,
        IActorStatePersister? persister)
    {
        _identity = identity;
        _stateStore = stateStore;
        _storeName = storeName;
        _keyFormatter = keyFormatter ?? ActorStateKeyFormatter.Instance;
        _persister = persister ?? new ActorStatePersister(stateStore);
    }

    public async ValueTask<TState?> GetStateAsync<TState>(string stateName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);

        if (_cache.TryGetValue(stateName, out var entry))
        {
            if (entry.Status == ActorStateStatus.Deleted)
            {
                return default;
            }

            return (TState?)entry.Value;
        }

        var key = _keyFormatter.FormatStateKey(_identity, stateName);
        var stored = await _stateStore.GetAsync<TState>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (stored.HasValue)
        {
            var loadedEntry = new ActorStateEntry(
                stored.Value.Value,
                stored.Value.ETag,
                ActorStateStatus.Unchanged,
                typeof(TState));

            _cache[stateName] = loadedEntry;
            return stored.Value.Value;
        }

        return default;
    }

    public ValueTask SetStateAsync<TState>(string stateName, TState value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);

        if (_cache.TryGetValue(stateName, out var entry))
        {
            entry.Value = value;
            entry.ValueType = typeof(TState);
            if (entry.Status != ActorStateStatus.Added)
            {
                entry.Status = ActorStateStatus.Modified;
            }
        }
        else
        {
            _cache[stateName] = new ActorStateEntry(
                value,
                null,
                ActorStateStatus.Added,
                typeof(TState));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TrySetStateAsync<TState>(string stateName, TState value, CancellationToken cancellationToken = default)
    {
        SetStateAsync(stateName, value, cancellationToken);
        return ValueTask.FromResult(true);
    }

    public async ValueTask<bool> RemoveStateAsync(string stateName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);

        if (_cache.TryGetValue(stateName, out var entry))
        {
            if (entry.Status == ActorStateStatus.Deleted)
            {
                return false;
            }

            if (entry.Status == ActorStateStatus.Added)
            {
                _cache.Remove(stateName);
                return true;
            }

            entry.Status = ActorStateStatus.Deleted;
            entry.Value = null;
            return true;
        }

        var key = _keyFormatter.FormatStateKey(_identity, stateName);
        var stored = await _stateStore.GetAsync<object>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (stored.HasValue)
        {
            _cache[stateName] = new ActorStateEntry(
                null,
                stored.Value.ETag,
                ActorStateStatus.Deleted,
                typeof(object));

            return true;
        }

        return false;
    }

    public async ValueTask<bool> ContainsStateAsync(string stateName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);

        if (_cache.TryGetValue(stateName, out var entry))
        {
            return entry.Status != ActorStateStatus.Deleted;
        }

        var key = _keyFormatter.FormatStateKey(_identity, stateName);
        var stored = await _stateStore.GetAsync<object>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        return stored.HasValue;
    }

    public async ValueTask SaveStateAsync(CancellationToken cancellationToken = default)
    {
        List<string>? keysToRemove = null;

        foreach (var (stateName, entry) in _cache)
        {
            var key = _keyFormatter.FormatStateKey(_identity, stateName);

            await _persister.PersistEntryAsync(
                _identity,
                _storeName,
                key,
                stateName,
                entry,
                cancellationToken).ConfigureAwait(false);

            if (entry.Status == ActorStateStatus.Deleted)
            {
                (keysToRemove ??= []).Add(stateName);
            }
        }

        if (keysToRemove is not null)
        {
            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }
        }
    }

    public ValueTask ClearCacheAsync()
    {
        _cache.Clear();
        return ValueTask.CompletedTask;
    }
}
