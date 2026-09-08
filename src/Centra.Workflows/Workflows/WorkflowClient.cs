using System.Reflection;
using Centra.Workflows;

namespace Centra.Core.Workflows;

/// <summary>
/// Client for starting, querying, awaiting, and managing distributed workflow instances.
/// </summary>
public sealed class WorkflowClient : IWorkflowClient
{
    private readonly IWorkflowEngine _engine;
    private readonly IWorkflowRegistry _registry;

    public WorkflowClient(IWorkflowEngine engine, IWorkflowRegistry registry)
    {
        _engine = engine;
        _registry = registry;
    }

    public ValueTask<WorkflowInstanceId> StartWorkflowAsync<TWorkflow, TInput>(
        TInput input,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
        where TWorkflow : class, IWorkflow
    {
        var workflowName = ResolveWorkflowName<TWorkflow>();
        return _engine.StartWorkflowAsync(workflowName, input, instanceId, cancellationToken);
    }

    public ValueTask<WorkflowInstanceId> StartWorkflowAsync(
        string workflowName,
        object? input,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        return _engine.StartWorkflowAsync(workflowName, input, instanceId, cancellationToken);
    }

    public ValueTask<WorkflowState?> GetWorkflowStateAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default)
    {
        return _engine.GetWorkflowStateAsync(instanceId, cancellationToken);
    }

    public ValueTask<TOutput> WaitForWorkflowCompletionAsync<TOutput>(
        WorkflowInstanceId instanceId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return _engine.WaitForWorkflowCompletionAsync<TOutput>(instanceId, timeout, cancellationToken);
    }

    public ValueTask RaiseEventAsync<TEvent>(
        WorkflowInstanceId instanceId,
        string eventName,
        TEvent eventData,
        CancellationToken cancellationToken = default)
    {
        return _engine.RaiseEventAsync(instanceId, eventName, eventData, cancellationToken);
    }

    public ValueTask TerminateWorkflowAsync(
        WorkflowInstanceId instanceId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        return _engine.TerminateWorkflowAsync(instanceId, reason, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<WorkflowHistoryEvent>> GetWorkflowHistoryAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default)
    {
        var records = await _engine.GetWorkflowHistoryAsync(instanceId, cancellationToken).ConfigureAwait(false);
        var result = new WorkflowHistoryEvent[records.Count];
        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            result[i] = new WorkflowHistoryEvent(
                r.EventId,
                (WorkflowHistoryEventType)r.EventType,
                r.Name,
                r.Timestamp,
                r.Data is not null ? new ReadOnlyMemory<byte>(r.Data) : ReadOnlyMemory<byte>.Empty,
                r.Details);
        }

        return result;
    }

    public ValueTask PurgeWorkflowAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default)
    {
        return _engine.PurgeWorkflowAsync(instanceId, cancellationToken);
    }

    private string ResolveWorkflowName<TWorkflow>()
    {
        var type = typeof(TWorkflow);
        var attr = type.GetCustomAttribute<WorkflowAttribute>();
        if (attr is not null && !string.IsNullOrWhiteSpace(attr.Name))
        {
            return attr.Name;
        }

        return type.Name;
    }
}
