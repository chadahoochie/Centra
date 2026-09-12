namespace Centra.Workflows;

/// <summary>
/// Defines the storage contract for persisting and querying durable workflow timers.
/// </summary>
public interface IDurableWorkflowTimerStore
{
    /// <summary>
    /// Persists a durable timer schedule for a suspended workflow.
    /// </summary>
    ValueTask SaveTimerAsync(DurableWorkflowTimerRecord timer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches all active timers whose due time is less than or equal to the specified timestamp.
    /// </summary>
    ValueTask<IReadOnlyList<DurableWorkflowTimerRecord>> GetDueTimersAsync(DateTimeOffset asOfUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a durable timer record after it has fired or when the workflow is cancelled/terminated.
    /// </summary>
    ValueTask DeleteTimerAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default);
}
