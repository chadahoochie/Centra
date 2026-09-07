namespace Centra.Workflows;

/// <summary>
/// Client interface for starting, querying, and managing distributed workflows.
/// </summary>
public interface IWorkflowClient
{
    ValueTask<WorkflowInstanceId> StartWorkflowAsync<TWorkflow, TInput>(
        TInput input,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
        where TWorkflow : class, IWorkflow;

    ValueTask<WorkflowInstanceId> StartWorkflowAsync(
        string workflowName,
        object? input,
        string? instanceId = null,
        CancellationToken cancellationToken = default);

    ValueTask<WorkflowState?> GetWorkflowStateAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default);

    ValueTask<TOutput> WaitForWorkflowCompletionAsync<TOutput>(
        WorkflowInstanceId instanceId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    ValueTask RaiseEventAsync<TEvent>(
        WorkflowInstanceId instanceId,
        string eventName,
        TEvent eventData,
        CancellationToken cancellationToken = default);

    ValueTask TerminateWorkflowAsync(
        WorkflowInstanceId instanceId,
        string reason,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<WorkflowHistoryEvent>> GetWorkflowHistoryAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default);

    ValueTask PurgeWorkflowAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default);
}
