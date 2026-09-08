using Centra.Workflows;

namespace Centra.Core.Workflows;

/// <summary>
/// Dispatches activity execution with resilience pipelines, timeout enforcement, and W3C context propagation.
/// </summary>
public interface IWorkflowActivityDispatcher
{
    ValueTask<TOutput> DispatchActivityAsync<TOutput>(
        WorkflowInstanceId instanceId,
        string activityName,
        object? input,
        ActivityOptions? options = null,
        CancellationToken cancellationToken = default);
}
