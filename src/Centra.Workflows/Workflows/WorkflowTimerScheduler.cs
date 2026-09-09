using System.Collections.Concurrent;
using Centra.Workflows;
using Microsoft.Extensions.Logging;

namespace Centra.Core.Workflows;

/// <summary>
/// Default implementation of <see cref="IWorkflowTimerScheduler"/> managing in-memory timers via <see cref="TimeProvider"/>.
/// </summary>
public sealed class WorkflowTimerScheduler : IWorkflowTimerScheduler
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<WorkflowInstanceId, ITimer> _activeTimers = new();

    public WorkflowTimerScheduler(TimeProvider? timeProvider = null, ILogger? logger = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public void ScheduleTimer(WorkflowInstanceId instanceId, TimeSpan delay, Func<WorkflowInstanceId, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (_activeTimers.TryRemove(instanceId, out var existing))
        {
            existing.Dispose();
        }

        var timer = _timeProvider.CreateTimer(
            async state =>
            {
                var id = (WorkflowInstanceId)state!;
                try
                {
                    await callback(id).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to fire timer for workflow '{InstanceId}'", id.Value);
                }
            },
            instanceId,
            delay,
            Timeout.InfiniteTimeSpan);

        _activeTimers[instanceId] = timer;
    }

    public bool CancelTimer(WorkflowInstanceId instanceId)
    {
        if (_activeTimers.TryRemove(instanceId, out var timer))
        {
            timer.Dispose();
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        foreach (var timer in _activeTimers.Values)
        {
            timer.Dispose();
        }
        _activeTimers.Clear();
    }
}
