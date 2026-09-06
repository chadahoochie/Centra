using Centra.Locks;

namespace Centra.Providers.InMemory.Locks;

public sealed class InMemoryDistributedLock : IDistributedLock
{
    private readonly InMemoryDistributedLockDriver _owner;
    private readonly string _storeName;
    private int _disposed;

    public string ResourceId { get; }
    public string LockId { get; }
    public DateTimeOffset ExpiresAt { get; internal set; }

    internal InMemoryDistributedLock(
        InMemoryDistributedLockDriver owner,
        string storeName,
        string resourceId,
        string lockId,
        DateTimeOffset expiresAt)
    {
        _owner = owner;
        _storeName = storeName;
        ResourceId = resourceId;
        LockId = lockId;
        ExpiresAt = expiresAt;
    }

    public ValueTask<bool> RenewAsync(TimeSpan additionalTime, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
        {
            return new ValueTask<bool>(false);
        }

        return _owner.RenewLockAsync(_storeName, ResourceId, LockId, additionalTime);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            return _owner.ReleaseLockAsync(_storeName, ResourceId, LockId);
        }

        return ValueTask.CompletedTask;
    }
}
