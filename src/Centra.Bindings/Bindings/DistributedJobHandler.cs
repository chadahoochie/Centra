using System.Diagnostics;
using Centra.Bindings;
using Centra.Diagnostics;
using Centra.Locks;
using Microsoft.Extensions.Logging;

namespace Centra.Bindings;

public sealed class DistributedJobHandler : IJobHandler
{
    private readonly IJobHandler _innerHandler;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly string _lockStoreName;
    private readonly ILogger<DistributedJobHandler>? _logger;
    private readonly TimeSpan _lockTimeout;

    public DistributedJobHandler(
        IJobHandler innerHandler,
        IDistributedLockProvider lockProvider,
        string lockStoreName = "lockstore",
        ILogger<DistributedJobHandler>? logger = null,
        TimeSpan? lockTimeout = null)
    {
        _innerHandler = innerHandler ?? throw new ArgumentNullException(nameof(innerHandler));
        _lockProvider = lockProvider ?? throw new ArgumentNullException(nameof(lockProvider));
        _lockStoreName = string.IsNullOrWhiteSpace(lockStoreName) ? "lockstore" : lockStoreName;
        _logger = logger;
        _lockTimeout = lockTimeout ?? TimeSpan.FromMinutes(2);
    }

    public async ValueTask ExecuteAsync(ScheduledJobContext context)
    {
        var resourceId = $"cron:{context.JobName}:{context.ScheduledTime.ToUnixTimeSeconds()}";

        var acquiredLock = await _lockProvider.TryAcquireLockAsync(
            _lockStoreName,
            resourceId,
            _lockTimeout,
            context.CancellationToken).ConfigureAwait(false);

        if (acquiredLock is null)
        {
            _logger?.LogInformation(
                "Cron job '{JobName}' tick at {ScheduledTime} skipped on this instance because another cluster replica acquired the lock.",
                context.JobName,
                context.ScheduledTime);
            CentraMeters.RecordBindingTrigger(context.JobName, "skipped", 0);
            return;
        }

        var startTime = Stopwatch.GetTimestamp();
        await using (acquiredLock.ConfigureAwait(false))
        {
            try
            {
                await _innerHandler.ExecuteAsync(context).ConfigureAwait(false);
                var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
                CentraMeters.RecordBindingTrigger(context.JobName, "success", durationMs);
            }
            catch
            {
                var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
                CentraMeters.RecordBindingTrigger(context.JobName, "error", durationMs);
                throw;
            }
        }
    }
}
