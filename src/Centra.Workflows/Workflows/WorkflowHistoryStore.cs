using Centra.State;
using Centra.Workflows;

namespace Centra.Core.Workflows;

/// <summary>
/// State-store-backed persistence for workflow instances and event streams.
/// </summary>
public sealed class WorkflowHistoryStore : IWorkflowHistoryStore
{
    private readonly IStateStore _stateStore;
    private readonly string _storeName;

    public WorkflowHistoryStore(IStateStore stateStore, string storeName)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);

        _stateStore = stateStore;
        _storeName = storeName;
    }

    public async ValueTask<WorkflowStateRecord?> GetStateAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default)
    {
        var key = FormatStateKey(instanceId);
        var entry = await _stateStore.GetAsync<WorkflowStateRecord>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        return entry.HasValue ? entry.Value.Value : null;
    }

    public async ValueTask SaveStateAsync(WorkflowStateRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var key = FormatStateKey(record.InstanceId);
        await _stateStore.SetAsync(_storeName, key, record, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<List<WorkflowHistoryEventRecord>> GetHistoryAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default)
    {
        var key = FormatHistoryKey(instanceId);
        var entry = await _stateStore.GetAsync<WorkflowHistoryRecord>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        return entry.HasValue ? entry.Value.Value.Events : [];
    }

    public async ValueTask AppendHistoryAsync(WorkflowInstanceId instanceId, IReadOnlyList<WorkflowHistoryEventRecord> newEvents, CancellationToken cancellationToken = default)
    {
        if (newEvents.Count == 0) return;

        var key = FormatHistoryKey(instanceId);
        var existing = await _stateStore.GetAsync<WorkflowHistoryRecord>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);

        WorkflowHistoryRecord record;
        string? etag = null;

        if (existing.HasValue)
        {
            record = existing.Value.Value;
            etag = existing.Value.ETag;
            record.Events.AddRange(newEvents);
        }
        else
        {
            record = new WorkflowHistoryRecord { Events = [.. newEvents] };
        }

        if (!string.IsNullOrEmpty(etag))
        {
            var success = await _stateStore.TrySetAsync(_storeName, key, record, etag, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!success)
            {
                // Fallback to unconditional set to guarantee event durability
                await _stateStore.SetAsync(_storeName, key, record, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await _stateStore.SetAsync(_storeName, key, record, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask PurgeAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default)
    {
        await _stateStore.DeleteAsync(_storeName, FormatStateKey(instanceId), cancellationToken: cancellationToken).ConfigureAwait(false);
        await _stateStore.DeleteAsync(_storeName, FormatHistoryKey(instanceId), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static string FormatStateKey(string instanceId) => $"wf:state:{instanceId}";
    internal static string FormatHistoryKey(string instanceId) => $"wf:history:{instanceId}";
}
