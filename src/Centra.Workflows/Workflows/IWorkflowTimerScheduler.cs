using Centra.Workflows;

namespace Centra.Core.Workflows;

/// <summary>
/// Defines a contract for scheduling and cancelling workflow timer callbacks.
/// </summary>
public interface IWorkflowTimerScheduler : IDisposable
{
    /// <summary>
    /// Schedules a timer callback for the given workflow instance after the specified delay.
    /// </summary>
    void ScheduleTimer(WorkflowInstanceId instanceId, TimeSpan delay, Func<WorkflowInstanceId, ValueTask> callback);

    /// <summary>
    /// Cancels and evicts any active timer registered for the workflow instance.
    /// </summary>
    bool CancelTimer(WorkflowInstanceId instanceId);
}
