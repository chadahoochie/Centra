using System.Diagnostics;

namespace Centra.Locks;

public static class DistributedLockHelper
{
    public static async ValueTask<IDistributedLock> AcquireLockAsync(
        IDistributedLockProvider provider,
        string lockStoreName,
        string resourceId,
        TimeSpan expiryTime,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockStoreName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var startTimestamp = Stopwatch.GetTimestamp();

        while (!cancellationToken.IsCancellationRequested)
        {
            var @lock = await provider.TryAcquireLockAsync(lockStoreName, resourceId, expiryTime, cancellationToken).ConfigureAwait(false);
            if (@lock is not null)
            {
                return @lock;
            }

            if (Stopwatch.GetElapsedTime(startTimestamp) >= timeout)
            {
                throw new TimeoutException($"Failed to acquire lock for resource '{resourceId}' in store '{lockStoreName}' within {timeout.TotalSeconds}s.");
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new OperationCanceledException(cancellationToken);
    }
}
