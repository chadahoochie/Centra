using System.Diagnostics;
using Centra.Diagnostics;

namespace Centra.Locks;

public sealed class TrackingDistributedLock : IDistributedLock
{
    private readonly IDistributedLock _innerLock;
    private readonly string _lockStoreName;
    private readonly long _acquiredTimestamp;
    private int _disposed;

    public TrackingDistributedLock(IDistributedLock innerLock, string lockStoreName)
    {
        _innerLock = innerLock ?? throw new ArgumentNullException(nameof(innerLock));
        ArgumentException.ThrowIfNullOrWhiteSpace(lockStoreName);
        _lockStoreName = lockStoreName;
        _acquiredTimestamp = Stopwatch.GetTimestamp();
    }

    public string ResourceId => _innerLock.ResourceId;

    public string LockId => _innerLock.LockId;

    public ValueTask<bool> RenewAsync(TimeSpan additionalTime, CancellationToken cancellationToken = default)
    {
        return _innerLock.RenewAsync(additionalTime, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _innerLock.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            var holdDurationMs = Stopwatch.GetElapsedTime(_acquiredTimestamp).TotalMilliseconds;
            CentraMeters.RecordLockHold(_lockStoreName, holdDurationMs);
        }
    }
}
