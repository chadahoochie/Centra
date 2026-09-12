using Centra.Locks;
using Centra.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Centra.Core.Workflows;

/// <summary>
/// Distributed coordinator that periodically sweeps due workflow timers from <see cref="IDurableWorkflowTimerStore"/>,
/// coordinates single-execution via <see cref="IDistributedLockProvider"/>, and fires turn resumptions.
/// </summary>
public sealed class DurableWorkflowTimerCoordinator
{
    private readonly IDurableWorkflowTimerStore _timerStore;
    private readonly Func<WorkflowInstanceId, ValueTask> _fireTimerCallback;
    private readonly IDistributedLockProvider? _lockProvider;
    private readonly string _lockStoreName;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DurableWorkflowTimerCoordinator> _logger;

    public DurableWorkflowTimerCoordinator(
        IDurableWorkflowTimerStore timerStore,
        Func<WorkflowInstanceId, ValueTask> fireTimerCallback,
        IDistributedLockProvider? lockProvider = null,
        string lockStoreName = "lockstore",
        TimeProvider? timeProvider = null,
        ILogger<DurableWorkflowTimerCoordinator>? logger = null)
    {
        _timerStore = timerStore ?? throw new ArgumentNullException(nameof(timerStore));
        _fireTimerCallback = fireTimerCallback ?? throw new ArgumentNullException(nameof(fireTimerCallback));
        _lockProvider = lockProvider;
        _lockStoreName = lockStoreName;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<DurableWorkflowTimerCoordinator>.Instance;
    }

    /// <summary>
    /// Sweeps all due timers and fires their workflow turn resumptions.
    /// Returns the number of timers processed.
    /// </summary>
    public async ValueTask<int> ProcessDueTimersAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var dueTimers = await _timerStore.GetDueTimersAsync(now, cancellationToken).ConfigureAwait(false);

        if (dueTimers.Count == 0)
        {
            return 0;
        }

        int processed = 0;
        foreach (var timer in dueTimers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lockResource = $"workflow:timer:{timer.InstanceId.Value}";
            IDistributedLock? acquiredLock = null;

            try
            {
                if (_lockProvider is not null)
                {
                    acquiredLock = await _lockProvider.TryAcquireLockAsync(
                        _lockStoreName,
                        lockResource,
                        TimeSpan.FromSeconds(30),
                        cancellationToken).ConfigureAwait(false);

                    if (acquiredLock is null)
                    {
                        // Another cluster replica is processing this timer tick
                        continue;
                    }
                }

                _logger.LogInformation("Firing durable timer for workflow '{InstanceId}' (due at {DueTimeUtc})",
                    timer.InstanceId.Value, timer.DueTimeUtc);

                await _fireTimerCallback(timer.InstanceId).ConfigureAwait(false);
                await _timerStore.DeleteTimerAsync(timer.InstanceId, cancellationToken).ConfigureAwait(false);
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process durable timer for workflow '{InstanceId}'", timer.InstanceId.Value);
            }
            finally
            {
                if (acquiredLock is not null)
                {
                    await acquiredLock.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        return processed;
    }
}
