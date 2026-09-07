namespace Centra.Workflows;

/// <summary>
/// Provides orchestration capabilities to running workflows.
/// </summary>
public interface IWorkflowContext
{
    WorkflowInstanceId InstanceId { get; }
    string WorkflowName { get; }
    DateTimeOffset CurrentUtcDateTime { get; }
    bool IsReplaying { get; }

    Guid NewGuid();

    ValueTask<TResult> CallActivityAsync<TActivity, TInput, TResult>(TInput input, ActivityOptions? options = null)
        where TActivity : class, IWorkflowActivity<TInput, TResult>;

    ValueTask<TResult> CallActivityAsync<TResult>(string activityName, object? input, ActivityOptions? options = null);

    ValueTask CreateTimerAsync(TimeSpan duration, CancellationToken cancellationToken = default);

    ValueTask<TEvent> WaitForExternalEventAsync<TEvent>(string eventName, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    IWorkflowSaga CreateSaga();

    void SetCustomStatus(string? status);
}
