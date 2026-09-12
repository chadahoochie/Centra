using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Drivers;
using Centra.Locks;
using Centra.Registry;

namespace Centra.Locks;

public sealed class CentraDistributedLockProvider : IDistributedLockProvider
{
    private readonly ComponentRegistry _registry;

    public CentraDistributedLockProvider(ComponentRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async ValueTask<IDistributedLock?> TryAcquireLockAsync(
        string lockStoreName,
        string resourceId,
        TimeSpan expiryTime,
        CancellationToken cancellationToken = default)
    {
        var driver = _registry.GetLockDriver(lockStoreName) ?? throw new InvalidOperationException($"No DistributedLock driver registered for lock store '{lockStoreName}'");
        var startTime = Stopwatch.GetTimestamp();
        using var activity = CentraDiagnostics.StartLockActivity("TryAcquire", lockStoreName, resourceId);

        try
        {
            var @lock = await driver.TryAcquireLockAsync(lockStoreName, resourceId, expiryTime, cancellationToken).ConfigureAwait(false);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordLockAcquisition(lockStoreName, @lock is not null ? "acquired" : "failed", durationMs);
            return @lock;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordLockAcquisition(lockStoreName, "error", durationMs);
            throw;
        }
    }

    public async ValueTask<IDistributedLock> AcquireLockAsync(
        string lockStoreName,
        string resourceId,
        TimeSpan expiryTime,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var driver = _registry.GetLockDriver(lockStoreName) ?? throw new InvalidOperationException($"No DistributedLock driver registered for lock store '{lockStoreName}'");
        var startTime = Stopwatch.GetTimestamp();
        using var activity = CentraDiagnostics.StartLockActivity("Acquire", lockStoreName, resourceId);

        try
        {
            var @lock = await driver.AcquireLockAsync(lockStoreName, resourceId, expiryTime, timeout, cancellationToken).ConfigureAwait(false);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordLockAcquisition(lockStoreName, "acquired", durationMs);
            return @lock;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordLockAcquisition(lockStoreName, "error", durationMs);
            throw;
        }
    }
}
