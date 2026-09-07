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
    private readonly Dictionary<string, ActorStateEntry> _cache = new(StringComparer.Ordinal);

    public ActorStateManager(ActorIdentity identity, IStateStore stateStore, string storeName)
    {
        _identity = identity;
        _stateStore = stateStore;
        _storeName = storeName;
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

        var key = FormatStateKey(stateName);
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

        var key = FormatStateKey(stateName);
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

        var key = FormatStateKey(stateName);
        var stored = await _stateStore.GetAsync<object>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        return stored.HasValue;
    }

    public async ValueTask SaveStateAsync(CancellationToken cancellationToken = default)
    {
        var keysToRemove = new List<string>();

        foreach (var (stateName, entry) in _cache)
        {
            var key = FormatStateKey(stateName);

            switch (entry.Status)
            {
                case ActorStateStatus.Added:
                    await _stateStore.SetAsync(
                        _storeName,
                        key,
                        entry.Value!,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    entry.Status = ActorStateStatus.Unchanged;
                    break;

                case ActorStateStatus.Modified:
                    if (!string.IsNullOrEmpty(entry.ETag))
                    {
                        var success = await _stateStore.TrySetAsync(
                            _storeName,
                            key,
                            entry.Value!,
                            entry.ETag,
                            cancellationToken: cancellationToken).ConfigureAwait(false);

                        if (!success)
                        {
                            throw new ActorConcurrencyException(
                                _identity,
                                stateName,
                                $"Optimistic concurrency conflict while updating actor state '{stateName}' for actor '{_identity}'.");
                        }
                    }
                    else
                    {
                        await _stateStore.SetAsync(
                            _storeName,
                            key,
                            entry.Value!,
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }

                    entry.Status = ActorStateStatus.Unchanged;
                    break;

                case ActorStateStatus.Deleted:
                    if (!string.IsNullOrEmpty(entry.ETag))
                    {
                        var success = await _stateStore.TryDeleteAsync(
                            _storeName,
                            key,
                            entry.ETag,
                            cancellationToken: cancellationToken).ConfigureAwait(false);

                        if (!success)
                        {
                            throw new ActorConcurrencyException(
                                _identity,
                                stateName,
                                $"Optimistic concurrency conflict while deleting actor state '{stateName}' for actor '{_identity}'.");
                        }
                    }
                    else
                    {
                        await _stateStore.DeleteAsync(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
                    }

                    keysToRemove.Add(stateName);
                    break;

                case ActorStateStatus.Unchanged:
                default:
                    // Zero redundant I/O for unmodified state
                    break;
            }
        }

        foreach (var key in keysToRemove)
        {
            _cache.Remove(key);
        }
    }

    public ValueTask ClearCacheAsync()
    {
        _cache.Clear();
        return ValueTask.CompletedTask;
    }

    private string FormatStateKey(string stateName) =>
        $"actors:{_identity.Type.Value}:{_identity.Id.Value}:{stateName}";
}
