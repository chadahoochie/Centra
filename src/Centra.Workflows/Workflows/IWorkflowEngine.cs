using Centra.Workflows;

namespace Centra.Core.Workflows;

/// <summary>
/// Core execution engine coordinating workflow lifecycle, deterministic replay, state, and sagas.
/// </summary>
public interface IWorkflowEngine
{
    ValueTask<WorkflowInstanceId> StartWorkflowAsync(
        string workflowName,
        object? input,
        string? instanceId = null,
        CancellationToken cancellationToken = default);

    ValueTask<WorkflowState?> GetWorkflowStateAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default);

    ValueTask<List<WorkflowHistoryEventRecord>> GetWorkflowHistoryAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default);

    ValueTask<TOutput> WaitForWorkflowCompletionAsync<TOutput>(
        WorkflowInstanceId instanceId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    ValueTask RaiseEventAsync(
        WorkflowInstanceId instanceId,
        string eventName,
        object? eventData,
        CancellationToken cancellationToken = default);

    ValueTask FireTimerAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default);

    ValueTask TerminateWorkflowAsync(
        WorkflowInstanceId instanceId,
        string reason,
        CancellationToken cancellationToken = default);

    ValueTask PurgeWorkflowAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default);
}
