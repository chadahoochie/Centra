using Centra.Workflows;

namespace Centra.Core.Workflows;

/// <summary>
/// Persists and queries workflow states and append-only event histories.
/// </summary>
public interface IWorkflowHistoryStore
{
    ValueTask<WorkflowStateRecord?> GetStateAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default);
    ValueTask SaveStateAsync(WorkflowStateRecord record, CancellationToken cancellationToken = default);
    ValueTask<List<WorkflowHistoryEventRecord>> GetHistoryAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default);
    ValueTask AppendHistoryAsync(WorkflowInstanceId instanceId, IReadOnlyList<WorkflowHistoryEventRecord> newEvents, CancellationToken cancellationToken = default);
    ValueTask PurgeAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default);
}
