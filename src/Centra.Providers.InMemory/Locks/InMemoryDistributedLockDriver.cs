using System.Collections.Concurrent;
using Centra.Drivers;
using Centra.Locks;

namespace Centra.Providers.InMemory.Locks;

public sealed class InMemoryDistributedLockDriver : IDistributedLockDriver
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, InMemoryDistributedLock>> _stores =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly TimeProvider _timeProvider;

    public InMemoryDistributedLockDriver(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<IDistributedLock?> TryAcquireLockAsync(
        string lockStoreName,
        string resourceId,
        TimeSpan expiryTime,
        CancellationToken cancellationToken = default)
    {
        var store = _stores.GetOrAdd(lockStoreName, static _ => new ConcurrentDictionary<string, InMemoryDistributedLock>(StringComparer.OrdinalIgnoreCase));
        var now = _timeProvider.GetUtcNow();
        var lockId = Guid.NewGuid().ToString("N");
        var newLock = new InMemoryDistributedLock(this, lockStoreName, resourceId, lockId, now + expiryTime);

        while (true)
        {
            if (store.TryGetValue(resourceId, out var existing))
            {
                if (existing.ExpiresAt > now)
                {
                    // Currently held and not expired
                    return new ValueTask<IDistributedLock?>((IDistributedLock?)null);
                }

                if (store.TryUpdate(resourceId, newLock, existing))
                {
                    return new ValueTask<IDistributedLock?>(newLock);
                }

                continue;
            }

            if (store.TryAdd(resourceId, newLock))
            {
                return new ValueTask<IDistributedLock?>(newLock);
            }
        }
    }

    public ValueTask<IDistributedLock> AcquireLockAsync(
        string lockStoreName,
        string resourceId,
        TimeSpan expiryTime,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        DistributedLockHelper.AcquireLockAsync(this, lockStoreName, resourceId, expiryTime, timeout, cancellationToken);

    internal ValueTask<bool> RenewLockAsync(string storeName, string resourceId, string lockId, TimeSpan additionalTime)
    {
        var store = _stores.GetOrAdd(storeName, static _ => new ConcurrentDictionary<string, InMemoryDistributedLock>(StringComparer.OrdinalIgnoreCase));
        if (store.TryGetValue(resourceId, out var existing) && existing.LockId == lockId)
        {
            var now = _timeProvider.GetUtcNow();
            if (existing.ExpiresAt > now)
            {
                existing.ExpiresAt = now + additionalTime;
                return new ValueTask<bool>(true);
            }
        }

        return new ValueTask<bool>(false);
    }

    internal ValueTask ReleaseLockAsync(string storeName, string resourceId, string lockId)
    {
        var store = _stores.GetOrAdd(storeName, static _ => new ConcurrentDictionary<string, InMemoryDistributedLock>(StringComparer.OrdinalIgnoreCase));
        if (store.TryGetValue(resourceId, out var existing) && existing.LockId == lockId)
        {
            store.TryRemove(new KeyValuePair<string, InMemoryDistributedLock>(resourceId, existing));
        }

        return ValueTask.CompletedTask;
    }
}
