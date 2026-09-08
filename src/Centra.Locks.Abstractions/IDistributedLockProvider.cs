namespace Centra.Locks;

public interface IDistributedLockProvider
{
    ValueTask<IDistributedLock?> TryAcquireLockAsync(
        string lockStoreName,
        string resourceId,
        TimeSpan expiryTime,
        CancellationToken cancellationToken = default);

    ValueTask<IDistributedLock> AcquireLockAsync(
        string lockStoreName,
        string resourceId,
        TimeSpan expiryTime,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return DistributedLockHelper.AcquireLockAsync(this, lockStoreName, resourceId, expiryTime, timeout, cancellationToken);
    }
}
