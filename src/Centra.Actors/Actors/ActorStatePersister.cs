using Centra.Actors;
using Centra.State;

namespace Centra.Core.Actors;

/// <summary>
/// Default implementation of <see cref="IActorStatePersister"/> coordinating persistence mutations and ETags.
/// </summary>
internal sealed class ActorStatePersister : IActorStatePersister
{
    private readonly IStateStore _stateStore;

    public ActorStatePersister(IStateStore stateStore)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public async ValueTask PersistEntryAsync(
        ActorIdentity identity,
        string storeName,
        string key,
        string stateName,
        ActorStateEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);

        switch (entry.Status)
        {
            case ActorStateStatus.Added:
                await _stateStore.SetAsync(
                    storeName,
                    key,
                    entry.Value!,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await RefreshETagAsync(storeName, key, entry, cancellationToken).ConfigureAwait(false);
                entry.Status = ActorStateStatus.Unchanged;
                break;

            case ActorStateStatus.Modified:
                if (string.IsNullOrEmpty(entry.ETag))
                {
                    await _stateStore.SetAsync(storeName, key, entry.Value!, cancellationToken: cancellationToken).ConfigureAwait(false);
                    await RefreshETagAsync(storeName, key, entry, cancellationToken).ConfigureAwait(false);
                    entry.Status = ActorStateStatus.Unchanged;
                    return;
                }

                var setSuccess = await _stateStore.TrySetAsync(
                    storeName,
                    key,
                    entry.Value!,
                    entry.ETag,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (!setSuccess)
                {
                    throw new ActorConcurrencyException(
                        identity,
                        stateName,
                        $"Optimistic concurrency conflict while updating actor state '{stateName}' for actor '{identity}'.");
                }

                await RefreshETagAsync(storeName, key, entry, cancellationToken).ConfigureAwait(false);
                entry.Status = ActorStateStatus.Unchanged;
                break;

            case ActorStateStatus.Deleted:
                if (string.IsNullOrEmpty(entry.ETag))
                {
                    await _stateStore.DeleteAsync(storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
                    return;
                }

                var deleteSuccess = await _stateStore.TryDeleteAsync(
                    storeName,
                    key,
                    entry.ETag,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (!deleteSuccess)
                {
                    throw new ActorConcurrencyException(
                        identity,
                        stateName,
                        $"Optimistic concurrency conflict while deleting actor state '{stateName}' for actor '{identity}'.");
                }
                break;

            case ActorStateStatus.Unchanged:
            default:
                break;
        }
    }

    /// <summary>
    /// Refreshes the ETag of the state entry from the state store after a successful mutation.
    /// </summary>
    public async ValueTask RefreshETagAsync(
        string storeName,
        string key,
        ActorStateEntry entry,
        CancellationToken cancellationToken)
    {
        var stored = await _stateStore.GetAsync<object>(storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (stored.HasValue)
        {
            entry.ETag = stored.Value.ETag;
        }
    }
}
